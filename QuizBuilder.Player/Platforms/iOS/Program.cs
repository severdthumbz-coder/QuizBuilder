using ObjCRuntime;
using UIKit;

namespace QuizBuilder.Player;

// The iOS process entry point. Equivalent to the implicit launcher Android sets
// up via [Application]/[Activity]; on iOS it is explicit. UIApplication.Main
// hands control to AppDelegate, which builds the MAUI app.
public class Program
{
    static void Main(string[] args)
    {
        UIApplication.Main(args, null, typeof(AppDelegate));
    }
}
