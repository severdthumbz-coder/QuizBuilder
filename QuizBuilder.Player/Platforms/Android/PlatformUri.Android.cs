using Android.Content;

namespace QuizBuilder.Player.Services;

public static partial class PlatformUri
{
    private static partial Task<Stream> OpenReadPlatformAsync(string uri, CancellationToken ct)
    {
        // A plain filesystem path (from the document picker's FileResult.
        // FullPath, say) is opened directly. Anything with a scheme goes
        // through the ContentResolver, which is the only thing that can read a
        // content:// URI's bytes.
        if (!uri.Contains("://", StringComparison.Ordinal) || uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            var path = uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
                ? new Uri(uri).LocalPath
                : uri;
            return Task.FromResult<Stream>(File.OpenRead(path));
        }

        var context = global::Android.App.Application.Context;
        var androidUri = global::Android.Net.Uri.Parse(uri)
            ?? throw new InvalidOperationException("The file location could not be understood.");

        var stream = context.ContentResolver?.OpenInputStream(androidUri)
            ?? throw new InvalidOperationException("The file could not be opened for reading.");

        return Task.FromResult<Stream>(stream);
    }
}
