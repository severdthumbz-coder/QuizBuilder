using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using QuizBuilder.Player.Services;

namespace QuizBuilder.Player;

// LaunchMode.SingleTop so an incoming "open .qbx" intent (VIEW) is delivered to
// the already-running activity via OnNewIntent rather than spawning a second
// copy of the app. The intent filters below are what make Quiz Player appear in
// the Android share sheet / "open with" list for .qbx files.
[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
                           ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
// A .qbx has no registered MIME type, so we match by file extension via a data
// scheme/pattern. Two filters cover the common delivery routes: tapping a file
// (VIEW) and receiving one from another app's share sheet (SEND).
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "content",
    DataMimeType = "*/*",
    DataPathPattern = ".*\\.qbx",
    DataHost = "*")]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "file",
    DataMimeType = "*/*",
    DataPathPattern = ".*\\.qbx",
    DataHost = "*")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        TryHandleIncomingFile(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        TryHandleIncomingFile(intent);
    }

    // Hand any incoming content:// or file:// .qbx URI to the shared import
    // pipeline. IncomingFileHandler copies it into the app sandbox (a content
    // URI is not a durable path) and raises an event the shell listens for.
    private static void TryHandleIncomingFile(Intent? intent)
    {
        if (intent?.Data is not { } uri) return;
        if (intent.Action != Intent.ActionView) return;

        // Android.Net.Uri.ToString() is annotated as possibly-null; skip if so.
        // (OfferAndroidUri also guards internally, but this keeps the call site
        // honest and silences CS8604.)
        var uriString = uri.ToString();
        if (string.IsNullOrEmpty(uriString)) return;

        IncomingFileHandler.OfferAndroidUri(uriString);
    }
}
