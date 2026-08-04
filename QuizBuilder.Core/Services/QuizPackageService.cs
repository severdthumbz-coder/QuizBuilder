using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <inheritdoc cref="IQuizPackageService"/>
public sealed class QuizPackageService : IQuizPackageService
{
    private const string ManifestEntry = "manifest.json";
    private const string QuizEntry = "quiz.json";
    private const string ImageDirectory = "images/";

    /// <summary>
    /// Guards against a zip-bomb or a hand-crafted archive: no single entry
    /// may exceed this when decompressed.
    /// </summary>
    private const long MaxEntryBytes = 64L * 1024 * 1024;

    /// <summary>Working set of images, keyed by package-relative path.</summary>
    private readonly Dictionary<string, byte[]> _images = new(StringComparer.OrdinalIgnoreCase);

    // Version 2 (v0.24): adds the Sequence question type. A v1 build cannot
    // deserialize a "sequence" $kind, so a file containing one is written as
    // version 2 and correctly rejected by older builds via the gate below.
    // Version 3 (v0.26): adds the Numeric and Dropdown question types, same
    // rationale — a file using either is written as version 3.
    public int CurrentFormatVersion => 3;

    public string AddImage(byte[] imageBytes, string originalFileName)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0)
            throw new ArgumentException("Image is empty.", nameof(imageBytes));

        // Content-addressed: identical bytes always land on the same path, so
        // the same picture on ten questions is stored once.
        var hash = Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant();
        var extension = SafeExtension(originalFileName);
        var relativePath = $"{ImageDirectory}{hash[..16]}{extension}";

        _images[relativePath] = imageBytes;
        return relativePath;
    }

    public byte[]? GetImage(string? relativePath) =>
        string.IsNullOrEmpty(relativePath) ? null
        : _images.TryGetValue(relativePath, out var bytes) ? bytes
        : null;

    public void ClearImageCache() => _images.Clear();

    /// <summary>
    /// Resolves an image path to a <c>data:</c> URI, or null if the image is not
    /// in the working set. Used by the HTML and self-grading exports, which are
    /// single files with nowhere to put a separate image -- the bytes ride along
    /// base64-encoded in the markup.
    /// </summary>
    public string? GetImageDataUri(string? relativePath)
    {
        var bytes = GetImage(relativePath);
        if (bytes is null) return null;

        var mime = MimeForPath(relativePath!);
        return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
    }

    private static string MimeForPath(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };

    private static string SafeExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) return ".bin";

        // Only allow known raster extensions; anything else is normalised.
        // Prevents a crafted filename injecting a path or an odd extension
        // into the archive.
        return ext.ToLowerInvariant() switch
        {
            ".png" => ".png",
            ".jpg" or ".jpeg" => ".jpg",
            ".gif" => ".gif",
            ".bmp" => ".bmp",
            ".webp" => ".webp",
            _ => ".bin"
        };
    }

    public async Task SaveAsync(
        QuizDocument document,
        string filePath,
        Func<string, byte[]?>? imageResolver = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        // Only images still referenced by a live question (or the theme logo)
        // get written. Deleting a question would otherwise leave its image in
        // the archive forever, growing the file on every save.
        var referenced = CollectReferencedImages(document);

        // Write to a temp file and move into place, so an interrupted save
        // never destroys the user's previous .qbx.
        var tempPath = filePath + ".tmp";

        try
        {
            await using (var fileStream = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
            {
                var manifest = new PackageManifest
                {
                    FormatVersion = CurrentFormatVersion,
                    AppVersion = typeof(QuizPackageService).Assembly
                        .GetName().Version?.ToString() ?? "0.0.0",
                    SavedUtc = DateTimeOffset.UtcNow,
                };

                await WriteJsonEntryAsync(archive, ManifestEntry, manifest, cancellationToken);
                await WriteJsonEntryAsync(archive, QuizEntry, document, cancellationToken);

                foreach (var relativePath in referenced)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var bytes = GetImage(relativePath) ?? imageResolver?.Invoke(relativePath);
                    if (bytes is null || bytes.Length == 0) continue;

                    var entry = archive.CreateEntry(relativePath, CompressionLevel.NoCompression);
                    // PNG/JPEG are already compressed; deflating them again
                    // costs CPU and saves ~nothing, so store them raw.

                    await using var entryStream = entry.Open();
                    await entryStream.WriteAsync(bytes, cancellationToken);
                }
            }

            if (File.Exists(filePath)) File.Delete(filePath);
            File.Move(tempPath, filePath);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch (IOException) { }
            }
            throw;
        }
    }

    /// <summary>
    /// Every image path currently reachable from the document. Anything not
    /// in this set is an orphan and is dropped on save.
    /// </summary>
    private static HashSet<string> CollectReferencedImages(QuizDocument document)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in document.Sections)
        foreach (var question in section.Questions)
        {
            if (!string.IsNullOrEmpty(question.ImageRelativePath))
                set.Add(question.ImageRelativePath);
        }

        foreach (var card in document.StudyCards)
        {
            if (!string.IsNullOrEmpty(card.FrontImageRelativePath)) set.Add(card.FrontImageRelativePath);
            if (!string.IsNullOrEmpty(card.BackImageRelativePath)) set.Add(card.BackImageRelativePath);
        }

        var logo = document.CustomTheme?.LogoRelativePath;
        if (!string.IsNullOrEmpty(logo)) set.Add(logo);

        return set;
    }

    public async Task<QuizPackageReadResult> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
            throw new QuizPackageException($"File not found: {filePath}");

        try
        {
            await using var fileStream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

            var manifest = await ReadJsonEntryAsync<PackageManifest>(
                archive, ManifestEntry, cancellationToken)
                ?? throw new QuizPackageException(
                    "This file is missing its manifest and may not be a .qbx file.");

            // Refuse the future rather than half-parsing it.
            if (manifest.FormatVersion > CurrentFormatVersion)
            {
                throw new QuizPackageException(
                    $"This quiz was saved by a newer version of Quiz Builder " +
                    $"(format {manifest.FormatVersion}; this build reads up to " +
                    $"{CurrentFormatVersion}). Please update to open it.");
            }

            var document = await ReadJsonEntryAsync<QuizDocument>(
                archive, QuizEntry, cancellationToken)
                ?? throw new QuizPackageException("This file does not contain a quiz.");

            _images.Clear();
            var warnings = new List<string>();

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!entry.FullName.StartsWith(ImageDirectory, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Zip-slip / zip-bomb guards. A .qbx is user-supplied data and
                // may have come from anywhere.
                if (entry.FullName.Contains("..", StringComparison.Ordinal))
                {
                    warnings.Add($"Skipped a suspicious entry path: {entry.FullName}");
                    continue;
                }

                if (entry.Length > MaxEntryBytes)
                {
                    warnings.Add($"Skipped oversized image: {entry.FullName}");
                    continue;
                }

                await using var entryStream = entry.Open();
                using var buffer = new MemoryStream();
                await entryStream.CopyToAsync(buffer, cancellationToken);
                _images[entry.FullName] = buffer.ToArray();
            }

            // Dangling references are a warning, not a failure: the quiz text
            // is intact and the user can re-attach the picture.
            foreach (var section in document.Sections)
            foreach (var question in section.Questions)
            {
                var path = question.ImageRelativePath;
                if (!string.IsNullOrEmpty(path) && !_images.ContainsKey(path))
                {
                    warnings.Add(
                        $"Question '{Truncate(question.Prompt, 40)}' references a " +
                        $"missing image and will display without it.");
                    question.ImageRelativePath = null;
                }
            }

            // Study-card images get the same treatment: a dangling reference is
            // cleared with a warning, so the card still shows its text.
            foreach (var card in document.StudyCards)
            {
                if (!string.IsNullOrEmpty(card.FrontImageRelativePath) && !_images.ContainsKey(card.FrontImageRelativePath))
                {
                    warnings.Add($"A study card ('{Truncate(card.Front, 40)}') references a missing front image.");
                    card.FrontImageRelativePath = null;
                }

                if (!string.IsNullOrEmpty(card.BackImageRelativePath) && !_images.ContainsKey(card.BackImageRelativePath))
                {
                    warnings.Add($"A study card ('{Truncate(card.Front, 40)}') references a missing back image.");
                    card.BackImageRelativePath = null;
                }
            }

            return new QuizPackageReadResult
            {
                Document = document,
                FormatVersion = manifest.FormatVersion,
                WrittenByAppVersion = manifest.AppVersion,
                SavedUtc = manifest.SavedUtc,
                Warnings = warnings,
            };
        }
        catch (InvalidDataException ex)
        {
            throw new QuizPackageException(
                "This file is not a valid .qbx archive or is corrupted.", ex);
        }
        catch (JsonException ex)
        {
            throw new QuizPackageException(
                "This .qbx file contains malformed quiz data.", ex);
        }
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? "(no prompt)"
        : value.Length <= max ? value
        : value[..max] + "...";

    private static async Task WriteJsonEntryAsync<T>(
        ZipArchive archive, string entryName, T value, CancellationToken ct)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, SettingsService.JsonOptions, ct);
    }

    private static async Task<T?> ReadJsonEntryAsync<T>(
        ZipArchive archive, string entryName, CancellationToken ct)
    {
        var entry = archive.GetEntry(entryName);
        if (entry is null) return default;

        if (entry.Length > MaxEntryBytes)
            throw new QuizPackageException($"Entry '{entryName}' is implausibly large.");

        await using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(stream, SettingsService.JsonOptions, ct);
    }

    private sealed class PackageManifest
    {
        public int FormatVersion { get; set; }
        public string? AppVersion { get; set; }
        public DateTimeOffset? SavedUtc { get; set; }
    }
}
