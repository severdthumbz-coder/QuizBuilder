using QuizBuilder.Player.Views;

namespace QuizBuilder.Player;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Routes for every page reachable by GoToAsync. The identity page is
        // the root ShellContent above and needs no registration; these are the
        // pushed pages.
        Routing.RegisterRoute("library", typeof(LibraryPage));
        Routing.RegisterRoute("home", typeof(HomePage));
        Routing.RegisterRoute("take", typeof(TakePage));
        Routing.RegisterRoute("results", typeof(ResultsPage));
        Routing.RegisterRoute("studycards", typeof(StudyCardsPage));
        Routing.RegisterRoute("history", typeof(HistoryPage));
        Routing.RegisterRoute("attempt", typeof(AttemptDetailPage));
    }
}
