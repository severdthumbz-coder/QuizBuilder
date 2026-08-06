using System.Windows;
using System.Windows.Controls;
using QuizBuilder.App.ViewModels;
using QuizBuilder.App.Views;
using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.App;

public partial class ShellWindow : Window
{
    private readonly INavigationService _navigation;
    private readonly ISettingsService _settings;

    /// <summary>
    /// Views by destination. Resolved from DI up front and kept alive, so each
    /// tab retains its state (scroll position, half-typed input) across
    /// navigation. Seven UserControls is a trivial memory cost against the
    /// alternative of rebuilding a tab every time it is shown.
    /// </summary>
    private readonly Dictionary<NavDestination, UIElement> _views = new();

    public ShellWindow(
        ShellViewModel viewModel,
        INavigationService navigation,
        ISettingsService settings,
        HelpView helpView,
        ThemeView themeView,
        SettingsView settingsView,
        QuizBuilderView quizBuilderView,
        PreviewView previewView,
        PublishView publishView,
        GitHubView gitHubView,
        TakeView takeView,
        FlashCardsView flashCardsView,
        StudyCardsView studyCardsView,
        ReviewView reviewView,
        QuestionBankView questionBankView)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        InitializeComponent();

        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        // Tabs register here as they are built; the placeholder covers the
        // rest so navigation stays usable throughout.
        RegisterView(NavDestination.Help, helpView);
        RegisterView(NavDestination.Theme, themeView);
        RegisterView(NavDestination.Settings, settingsView);
        RegisterView(NavDestination.QuizBuilder, quizBuilderView);
        RegisterView(NavDestination.Preview, previewView);
        RegisterView(NavDestination.Publish, publishView);
        RegisterView(NavDestination.GitHub, gitHubView);
        RegisterView(NavDestination.Take, takeView);
        RegisterView(NavDestination.QuestionBank, questionBankView);
        RegisterView(NavDestination.FlashCards, flashCardsView);
        RegisterView(NavDestination.StudyCards, studyCardsView);
        RegisterView(NavDestination.Review, reviewView);

        foreach (var destination in Enum.GetValues<NavDestination>())
        {
            if (!_views.ContainsKey(destination))
                RegisterView(destination, CreatePlaceholder(destination));
        }

        _navigation.Navigated += OnNavigated;

        ShowOnly(_navigation.Current);

        RestoreWindowPlacement();

        // Note: no focus juggling here. The nav rail's ring is driven by
        // Controls.FocusVisible, which tracks the last input device, so it
        // stays hidden until the user actually presses a key. Moving focus on
        // load was the earlier attempt and did not work: focus is reassigned
        // when the window activates, after Loaded has already run.
    }

    private void RegisterView(NavDestination destination, UIElement view)
    {
        view.Visibility = Visibility.Collapsed;
        ContentHost.Children.Add(view);
        _views[destination] = view;
    }

    private void OnNavigated(object? sender, NavigationChangedEventArgs e)
    {
        ShowOnly(e.Current);
    }

    private void ShowOnly(NavDestination destination)
    {
        foreach (var (key, view) in _views)
            view.Visibility = key == destination ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Stand-in for a tab that has not been built yet. Says so plainly rather
    /// than rendering an empty panel that looks like a bug.
    /// </summary>
    private UIElement CreatePlaceholder(NavDestination destination)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 420,
        };

        var heading = new TextBlock
        {
            Text = $"{Humanise(destination)} is not built yet",
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        };
        heading.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Family");
        heading.SetResourceReference(TextBlock.FontSizeProperty, "Font.Size.Title");
        heading.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");

        var detail = new TextBlock
        {
            Text = "This tab arrives in a later slice. The Help tab lists what works today.",
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        };
        detail.SetResourceReference(TextBlock.FontFamilyProperty, "Font.Family");
        detail.SetResourceReference(TextBlock.FontSizeProperty, "Font.Size.Body");
        detail.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");

        panel.Children.Add(heading);
        panel.Children.Add(detail);
        return panel;
    }

    private static string Humanise(NavDestination destination) => destination switch
    {
        NavDestination.QuizBuilder => "Quiz Builder",
        NavDestination.GitHub => "GitHub",
        _ => destination.ToString()
    };

    private void RestoreWindowPlacement()
    {
        var shell = _settings.Current.Shell;

        // Guard against a settings file that remembers a size larger than the
        // current display, or a zero from a corrupt write.
        if (shell.WindowWidth >= MinWidth && shell.WindowWidth <= SystemParameters.VirtualScreenWidth)
            Width = shell.WindowWidth;

        if (shell.WindowHeight >= MinHeight && shell.WindowHeight <= SystemParameters.VirtualScreenHeight)
            Height = shell.WindowHeight;

        if (shell.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        var shell = _settings.Current.Shell;

        // RestoreBounds reports the pre-maximise size; ActualWidth would record
        // the maximised dimensions and the window would never un-maximise to
        // anything sensible.
        if (WindowState == WindowState.Maximized)
        {
            shell.WindowMaximized = true;
            if (!RestoreBounds.IsEmpty)
            {
                shell.WindowWidth = RestoreBounds.Width;
                shell.WindowHeight = RestoreBounds.Height;
            }
        }
        else
        {
            shell.WindowMaximized = false;
            shell.WindowWidth = ActualWidth;
            shell.WindowHeight = ActualHeight;
        }

        _settings.Save();

        base.OnClosing(e);
    }
}
