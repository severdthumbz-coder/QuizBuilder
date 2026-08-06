namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// Stable identifiers for the seven destinations. Used as the persistence key
/// for the last-active tab, so these strings must not change casually.
/// </summary>
public enum NavDestination
{
    QuizBuilder,
    Settings,
    Theme,
    Preview,

    /// <summary>Sit the quiz, and see how past sittings went.</summary>
    Take,

    /// <summary>Author front/back study cards that feed the flash cards.</summary>
    StudyCards,

    /// <summary>Review questions as flip cards. No grading.</summary>
    FlashCards,

    /// <summary>Spaced-repetition study of the flash cards, with grading.</summary>
    Review,

    /// <summary>A reusable pool of questions to draw into quizzes.</summary>
    QuestionBank,

    Publish,
    GitHub,
    Help
}

/// <summary>
/// Owns which destination is active. Deliberately does NOT know about any
/// ViewModel type -- the UI layer maps destinations to views. This keeps
/// Core free of a reference to the tab implementations.
/// </summary>
public interface INavigationService
{
    NavDestination Current { get; }

    /// <summary>Raised after <see cref="Current"/> changes.</summary>
    event EventHandler<NavigationChangedEventArgs>? Navigated;

    void NavigateTo(NavDestination destination);
}

public sealed class NavigationChangedEventArgs : EventArgs
{
    public NavigationChangedEventArgs(NavDestination previous, NavDestination current)
    {
        Previous = previous;
        Current = current;
    }

    public NavDestination Previous { get; }
    public NavDestination Current { get; }
}
