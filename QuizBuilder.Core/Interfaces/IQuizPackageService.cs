using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// Reads and writes .qbx (Quiz Builder Exchange) files.
///
/// FORMAT -- a ZIP archive containing:
///
///   manifest.json     Required. { formatVersion, appVersion, savedUtc }
///   quiz.json         Required. The serialized QuizDocument.
///   images/           Optional. Attachments, named by content hash.
///   assets/logo.*     Optional. Custom theme logo.
///
/// ZIP rather than plain JSON because questions carry image attachments.
/// Base64-inlining them would inflate the file ~33% and produce
/// multi-megabyte single-line JSON that no editor can open.
///
/// Images are named by SHA-256 of their content, which de-duplicates the same
/// picture used across several questions for free, and makes writes idempotent.
///
/// COMPATIBILITY: FormatVersion gates loading. A file written by a newer
/// version than this build understands is rejected with a clear message
/// rather than being partially parsed into a corrupt document.
/// </summary>
public interface IQuizPackageService
{
    /// <summary>Format version this build writes and can read.</summary>
    int CurrentFormatVersion { get; }

    /// <summary>
    /// Writes the document to a .qbx file. Image bytes are resolved through
    /// <paramref name="imageResolver"/>, which maps a package-relative path to
    /// the bytes to store; returning null drops that reference.
    /// </summary>
    Task SaveAsync(
        QuizDocument document,
        string filePath,
        Func<string, byte[]?>? imageResolver = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a .qbx file. Throws <see cref="QuizPackageException"/> for a
    /// missing/corrupt archive or an unsupported future format version.
    /// </summary>
    Task<QuizPackageReadResult> LoadAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds an image to the working set, returning its package-relative path.
    /// The path is content-derived, so adding identical bytes twice yields
    /// the same path and stores one copy.
    /// </summary>
    string AddImage(byte[] imageBytes, string originalFileName);

    /// <summary>Retrieves image bytes previously added or loaded, or null.</summary>
    byte[]? GetImage(string? relativePath);

    /// <summary>An image resolved to a data: URI for single-file HTML, or null.</summary>
    string? GetImageDataUri(string? relativePath);

    /// <summary>Drops all cached images. Called when a new document starts.</summary>
    void ClearImageCache();
}

public sealed class QuizPackageReadResult
{
    public required QuizDocument Document { get; init; }
    public required int FormatVersion { get; init; }
    public string? WrittenByAppVersion { get; init; }
    public DateTimeOffset? SavedUtc { get; init; }

    /// <summary>
    /// Non-fatal problems found while reading -- e.g. a question referencing
    /// an image that isn't in the archive. Surfaced to the user rather than
    /// thrown, so a slightly damaged file still opens.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed class QuizPackageException : Exception
{
    public QuizPackageException(string message) : base(message) { }
    public QuizPackageException(string message, Exception inner) : base(message, inner) { }
}
