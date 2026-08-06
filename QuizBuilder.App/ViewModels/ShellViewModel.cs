using QuizBuilder.Core;
using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.App.ViewModels;

/// <summary>
/// One entry in the navigation rail.
///
/// IsActive is a property rather than a computed comparison so WPF can bind to
/// it directly and get change notification. The alternative -- binding each
/// item's highlight to a converter comparing against the shell's current
/// destination -- means every item must be re-evaluated on every change, and
/// converters cannot raise PropertyChanged.
/// </summary>
public sealed class NavItem : ViewModelBase
{
    private bool _isActive;

    public NavItem(NavDestination destination, string label, string glyph, string tooltip)
    {
        Destination = destination;
        Label = label;
        Glyph = glyph;
        Tooltip = tooltip;
    }

    public NavDestination Destination { get; }

    public string Label { get; }

    /// <summary>
    /// Segoe MDL2 Assets glyph codepoint. Present on every Windows 10+ install,
    /// so no icon font needs shipping. Icon-only controls would need
    /// AutomationProperties.Name for screen readers; the rail shows labels
    /// alongside, so the label carries the accessible name.
    /// </summary>
    public string Glyph { get; }

    public string Tooltip { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}

/// <summary>
/// Owns the navigation rail and which destination is showing.
///
/// Deliberately NOT a God object: it holds no quiz, settings, theme or export
/// logic. It knows the list of destinations and which one is active. Each tab's
/// own ViewModel owns that tab's behaviour, and shared state lives in
/// IQuizDocumentService / ISettingsService.
/// </summary>
public sealed class ShellViewModel : ViewModelBase
{
    private readonly INavigationService _navigation;
    private readonly ISettingsService _settings;

    public ShellViewModel(INavigationService navigation, ISettingsService settings)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        Items = new[]
        {
            new NavItem(NavDestination.QuizBuilder, "Quiz Builder", "\uE70F", "Build questions and sections"),
            new NavItem(NavDestination.Settings,    "Settings",     "\uE713", "Grading, selection and timing"),
            new NavItem(NavDestination.Theme,       "Theme",        "\uE790", "Colours, type and layout"),
            new NavItem(NavDestination.Preview,     "Preview",      "\uE890", "See the quiz as students will"),
            new NavItem(NavDestination.Take,        "Take",         "\uE73E", "Sit the quiz and see past results"),
            new NavItem(NavDestination.StudyCards,  "Study Cards",  "\uE70B", "Author front/back cards for the flash cards"),
            new NavItem(NavDestination.FlashCards,  "Flash Cards",  "\uE8F1", "Flip through questions and answers"),
            new NavItem(NavDestination.Review,      "Review",       "\uE81C", "Spaced-repetition study of your cards"),
            new NavItem(NavDestination.QuestionBank, "Question Bank", "\uE8F4", "A reusable pool of questions"),
            new NavItem(NavDestination.Publish,     "Publish",      "\uEDE1", "Export to PDF, Word, Excel or web"),
            new NavItem(NavDestination.GitHub,      "GitHub",       "\uE8AB", "Push and publish to GitHub Pages"),
            new NavItem(NavDestination.Help,        "Help",         "\uE897", "Features, workflow and version history"),
        };

        NavigateCommand = new RelayCommand(param =>
        {
            if (param is NavItem item) _navigation.NavigateTo(item.Destination);
        });

        _navigation.Navigated += OnNavigated;

        // Restore the last active tab, then sync the rail's highlight to it.
        _navigation.NavigateTo(_settings.Current.Shell.LastActiveTab);
        UpdateActiveStates(_navigation.Current);
    }

    public IReadOnlyList<NavItem> Items { get; }

    public RelayCommand NavigateCommand { get; }

    public string Title => $"Quiz Builder {VersionInfo.Display}";

    public NavDestination Current => _navigation.Current;

    private void OnNavigated(object? sender, NavigationChangedEventArgs e)
    {
        UpdateActiveStates(e.Current);
        OnPropertyChanged(nameof(Current));

        // Remember the tab across restarts. Written to settings.json on exit
        // rather than on every click, to avoid a disk write per navigation.
        _settings.Current.Shell.LastActiveTab = e.Current;
    }

    private void UpdateActiveStates(NavDestination current)
    {
        foreach (var item in Items)
            item.IsActive = item.Destination == current;
    }
}
