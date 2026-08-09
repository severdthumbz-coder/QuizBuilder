namespace QuizBuilder.Player.Services;

public static partial class PlatformUri
{
    private static partial Task<Stream> OpenReadPlatformAsync(string uri, CancellationToken ct)
    {
        // iOS has no ContentResolver: incoming .qbx files arrive as plain
        // filesystem paths (from the document picker's FileResult.FullPath) or
        // as file:// URLs (from "open in" / share). Both resolve to a local
        // path we can open directly. Anything else with a scheme is unexpected
        // on this platform, so treat it as a path and let File.OpenRead surface
        // a clear error rather than guessing.
        var path = uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? new Uri(uri).LocalPath
            : uri;

        return Task.FromResult<Stream>(File.OpenRead(path));
    }
}
