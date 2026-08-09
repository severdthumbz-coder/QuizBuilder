using Foundation;

namespace QuizBuilder.Player;

// The iOS counterpart to Android's MainApplication: it hands MAUI the same
// CreateMauiApp() the whole app is built from, so both platforms boot the
// identical DI container, services, and shell. Incoming-.qbx handling on iOS is
// declared in Info.plist (document types) and delivered through the standard
// MAUI lifecycle rather than an Android-style intent filter.
[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
