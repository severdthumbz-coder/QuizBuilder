using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;

namespace QuizBuilder.Player.Services;

/// <summary>The outcome of importing a .qbx: the loaded quiz, or a message.</summary>
public sealed class ImportOutcome
{
    public bool Success { get; private init; }
    public string? ErrorMessage { get; private init; }

    /// <summary>The package service that loaded the file, holding its image
    /// working set. Kept so the take screen can resolve question images via
    /// GetImage without reloading the archive.</summary>
    public IQuizPackageService? Package { get; private init; }

    public QuizPackageReadResult? Result { get; private init; }

    public static ImportOutcome Ok(IQuizPackageService package, QuizPackageReadResult result) =>
        new() { Success = true, Package = package, Result = result };

    public static ImportOutcome Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}

public interface IQbxImporter
{
    /// <summary>
    /// Copies the file behind a platform URI into the app sandbox, loads it, and
    /// files it in the library (adding or updating by quiz id). On success the
    /// outcome's Package/Result are the loaded quiz, ready to become the current
    /// session. The URI may be a content:// (SAF) or file:// string from an
    /// intent, or a FileResult path from the document picker.
    /// </summary>
    Task<ImportOutcome> ImportFromUriAsync(string uri, CancellationToken ct = default);

    /// <summary>Loads a quiz already stored in the library, by its file path.
    /// No copy and no re-index -- just opens what the library screen chose.</summary>
    Task<ImportOutcome> LoadFromLibraryAsync(string libraryFilePath, CancellationToken ct = default);
}

/// <summary>
/// Brings a .qbx into the app and loads it through the SAME Core service the
/// desktop uses. The only mobile-specific work is turning a platform URI into a
/// durable local path inside the app sandbox -- a content:// URI is not a file
/// path and may not survive the app being backgrounded, so we copy its bytes in
/// first. Everything after that is Core.
///
/// <para>
/// Imports are kept: a successful import is filed in the <see cref="QuizLibraryService"/>
/// under its quiz id, so the taker picks it from a list next time instead of
/// re-importing. This replaced the old throwaway-copy-then-prune model.
/// </para>
/// </summary>
public sealed class QbxImporter : IQbxImporter
{
    private readonly QuizLibraryService _library;

    public QbxImporter(QuizLibraryService library)
    {
        _library = library;
    }

    // An app-private, sandboxed directory that exists on every platform. This is
    // the storage-path adaptation the HANDOFF calls out -- Core writes "beside
    // the exe" on desktop, but a phone has no such place.
    private string SandboxDir => FileSystem.AppDataDirectory;

    public async Task<ImportOutcome> ImportFromUriAsync(string uri, CancellationToken ct = default)
    {
        string? tempPath = null;
        try
        {
            // Land the bytes in a TEMP file first: we do not know the quiz id
            // (needed for the permanent name) until the file is parsed, and a
            // failed parse should leave nothing behind.
            tempPath = await CopyUriToTempAsync(uri, ct);

            var outcome = await LoadAsync(tempPath, ct);
            if (!outcome.Success || outcome.Result is null)
                return outcome; // parse failed; temp is cleaned in finally

            // File it in the library under the quiz's own id. This copies the
            // temp file to quiz_<id>.qbx and adds/updates the index row. Re-
            // importing the same quiz updates the one entry rather than
            // duplicating, and keeps its history/paused (same id) attached.
            var doc = outcome.Result.Document;
            _library.AddOrUpdate(doc.Id, doc.Title, doc.QuestionCount, tempPath);

            return outcome;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ImportOutcome.Fail($"Couldn't open that file. {ex.Message}");
        }
        finally
        {
            // The temp copy has served its purpose (parsed, and its bytes copied
            // into the library on success). Remove it either way.
            if (tempPath is not null)
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best effort */ }
            }
        }
    }

    public Task<ImportOutcome> LoadFromLibraryAsync(string libraryFilePath, CancellationToken ct = default)
        => LoadAsync(libraryFilePath, ct);

    private async Task<ImportOutcome> LoadAsync(string localPath, CancellationToken ct)
    {
        try
        {
            // A fresh package service per load: it holds the loaded quiz's image
            // working set, and we do not want a previous quiz's images bleeding
            // into a new one.
            var package = new QuizPackageService();
            var result = await package.LoadAsync(localPath, ct);
            return ImportOutcome.Ok(package, result);
        }
        catch (QuizPackageException ex)
        {
            // Core already phrases these for humans (wrong format, corrupt,
            // future version). Surface the message as-is.
            return ImportOutcome.Fail(ex.Message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ImportOutcome.Fail($"Couldn't read the quiz. {ex.Message}");
        }
    }

    /// <summary>
    /// Streams the bytes behind a platform URI into a throwaway temp file and
    /// returns its path. Uses the MAUI FileSystem opener, which handles
    /// content://, file://, and asset URIs uniformly.
    /// </summary>
    private async Task<string> CopyUriToTempAsync(string uri, CancellationToken ct)
    {
        Directory.CreateDirectory(SandboxDir);

        var destination = Path.Combine(SandboxDir, $"import_tmp_{Guid.NewGuid():N}.qbx");

        await using var source = await OpenPlatformUriAsync(uri, ct);
        await using var dest = File.Create(destination);
        await source.CopyToAsync(dest, ct);

        return destination;
    }

    // Isolated so the platform-specific opening is in one place.
    private static Task<Stream> OpenPlatformUriAsync(string uri, CancellationToken ct)
        => PlatformUri.OpenReadAsync(uri, ct);
}
