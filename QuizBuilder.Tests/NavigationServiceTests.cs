using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

public class NavigationServiceTests
{
    [Fact]
    public void NavigateTo_ChangesCurrentAndRaisesEvent()
    {
        var svc = new NavigationService();
        NavigationChangedEventArgs? observed = null;
        svc.Navigated += (_, e) => observed = e;

        svc.NavigateTo(NavDestination.Help);

        Assert.Equal(NavDestination.Help, svc.Current);
        Assert.NotNull(observed);
        Assert.Equal(NavDestination.QuizBuilder, observed!.Previous);
        Assert.Equal(NavDestination.Help, observed.Current);
    }

    [Fact]
    public void NavigateTo_SameDestination_DoesNotRaise()
    {
        var svc = new NavigationService();
        svc.NavigateTo(NavDestination.Help);

        var raised = 0;
        svc.Navigated += (_, _) => raised++;

        svc.NavigateTo(NavDestination.Help);

        // Re-raising would make every tab re-run its activation work on a no-op
        // click, which is a real cost once a tab does work when it becomes visible.
        Assert.Equal(0, raised);
    }

    [Fact]
    public void DefaultDestination_IsQuizBuilder()
    {
        Assert.Equal(NavDestination.QuizBuilder, new NavigationService().Current);
    }

    [Fact]
    public void EveryDestination_IsReachable()
    {
        var svc = new NavigationService();

        foreach (var destination in Enum.GetValues<NavDestination>())
        {
            svc.NavigateTo(destination);
            Assert.Equal(destination, svc.Current);
        }
    }
}
