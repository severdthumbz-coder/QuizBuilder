namespace QuizBuilder.Player.Services;

/// <summary>
/// A tiny bridge between the Android activity (which receives "open .qbx"
/// intents and is constructed by the OS, outside our DI container) and the
/// running UI. The activity calls <see cref="OfferAndroidUri"/>; whatever part
/// of the app is listening picks it up.
///
/// <para>
/// Static because the intent can arrive before any page or view model exists
/// (cold start via "open with"). The latest offered URI is held so a listener
/// that subscribes slightly later still sees it, rather than missing a cold-
/// start file because it wasn't listening at the exact moment.
/// </para>
/// </summary>
public static class IncomingFileHandler
{
    private static readonly object Gate = new();
    private static string? _pending;

    /// <summary>
    /// Raised when a platform hands the app a file URI to open. The argument is
    /// the raw platform URI string (content:// or file://); resolving it to
    /// real bytes is the listener's job, via <see cref="IQbxImporter"/>.
    /// </summary>
    public static event Action<string>? FileOffered;

    /// <summary>Called by the Android activity for an incoming VIEW intent.</summary>
    public static void OfferAndroidUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return;

        lock (Gate) _pending = uri;

        // Marshal to the UI thread: the activity calls this from its own
        // lifecycle callback, and listeners will touch UI / navigation.
        var handler = FileOffered;
        if (handler is null) return;

        MainThread.BeginInvokeOnMainThread(() => handler.Invoke(uri));
    }

    /// <summary>
    /// A listener that comes online after a cold-start intent calls this to
    /// collect any URI that arrived before it subscribed. Returns null (and
    /// clears nothing) when there is nothing pending.
    /// </summary>
    public static string? TakePending()
    {
        lock (Gate)
        {
            var p = _pending;
            _pending = null;
            return p;
        }
    }
}
