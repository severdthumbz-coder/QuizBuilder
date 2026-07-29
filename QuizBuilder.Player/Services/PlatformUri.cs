namespace QuizBuilder.Player.Services;

/// <summary>
/// Opens a read stream for a platform file URI. The implementation is
/// per-platform (Android uses the ContentResolver for content:// URIs); this
/// shared half declares the contract and provides the fallback for plain paths.
/// </summary>
public static partial class PlatformUri
{
    public static Task<Stream> OpenReadAsync(string uri, CancellationToken ct)
        => OpenReadPlatformAsync(uri, ct);

    // Implemented in Platforms/Android/PlatformUri.Android.cs. The partial split
    // keeps Android types (Android.Net.Uri, ContentResolver) out of shared code.
    // A value-returning partial method must carry an explicit accessibility
    // modifier (CS8796); both halves use 'private static'.
    private static partial Task<Stream> OpenReadPlatformAsync(string uri, CancellationToken ct);
}
