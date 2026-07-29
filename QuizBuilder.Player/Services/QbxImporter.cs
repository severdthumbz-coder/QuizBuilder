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
    /// Copies the file behind a platform URI into the app sandbox and loads it.
    /// The URI may be a content:// (SAF) or file:// string from an intent, or a
    /// FileResult path from the document picker.
    /// </summary>
    Task<ImportOutcome> ImportFromUriAsync(string uri, CancellationToken ct = default);

    /// <summary>Imports from an already-resolved local file path.</summary>
    Task<ImportOutcome> ImportFromPathAsync(string localPath, CancellationToken ct = default);
}

/// <summary>
/// Brings a .qbx into the app and loads it through the SAME Core service the
/// desktop uses. The only mobile-specific work is turning a platform URI into a
/// durable local path inside the app sandbox -- a content:// URI is not a file
/// path and may not survive the app being backgrounded, so we copy its bytes in
/// first. Everything after that is Core.
/// </summary>
public sealed class QbxImporter : IQbxImporter
{
    // Imported quizzes are copied here: an app-private, sandboxed directory that
    // exists on every platform. This is the storage-path adaptation the HANDOFF
    // calls out -- Core writes "beside the exe" on desktop, but a phone has no
    // such place, so the player supplies FileSystem.AppDataDirectory instead.
    private string SandboxDir => FileSystem.AppDataDirectory;

    public async Task<ImportOutcome> ImportFromUriAsync(string uri, CancellationToken ct = default)
    {
        try
        {
            var localPath = await CopyUriToSandboxAsync(uri, ct);
            return await LoadAsync(localPath, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ImportOutcome.Fail($"Couldn't open that file. {ex.Message}");
        }
    }

    public Task<ImportOutcome> ImportFromPathAsync(string localPath, CancellationToken ct = default)
        => LoadAsync(localPath, ct);

    private async Task<ImportOutcome> LoadAsync(string localPath, CancellationToken ct)
    {
        try
        {
            // A fresh package service per import: it holds the loaded quiz's
            // image working set, and we do not want a previous quiz's images
            // bleeding into a new one.
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
    /// Streams the bytes behind a platform URI into a uniquely-named file in the
    /// sandbox and returns that path. Uses the MAUI FileSystem opener, which
    /// handles content://, file://, and asset URIs uniformly.
    /// </summary>
    private async Task<string> CopyUriToSandboxAsync(string uri, CancellationToken ct)
    {
        Directory.CreateDirectory(SandboxDir);

        // A stable-ish but collision-free name; the original name is not
        // trustworthy (content URIs often lack one) and not needed.
        var destination = Path.Combine(SandboxDir, $"import_{Guid.NewGuid():N}.qbx");

        await using var source = await OpenPlatformUriAsync(uri, ct);
        await using var dest = File.Create(destination);
        await source.CopyToAsync(dest, ct);

        return destination;
    }

    // Isolated so the platform-specific opening is in one place. FileSystem
    // .OpenAppPackageFileAsync is for bundled assets; for arbitrary user URIs we
    // go through the shared opener below.
    private static Task<Stream> OpenPlatformUriAsync(string uri, CancellationToken ct)
        => PlatformUri.OpenReadAsync(uri, ct);
}
