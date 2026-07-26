using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.Core.Services;

/// <inheritdoc cref="INavigationService"/>
public sealed class NavigationService : INavigationService
{
    public NavDestination Current { get; private set; } = NavDestination.QuizBuilder;

    public event EventHandler<NavigationChangedEventArgs>? Navigated;

    public void NavigateTo(NavDestination destination)
    {
        if (destination == Current) return;

        var previous = Current;
        Current = destination;

        Navigated?.Invoke(this, new NavigationChangedEventArgs(previous, destination));
    }
}
