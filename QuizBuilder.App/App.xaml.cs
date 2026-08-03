using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using QuizBuilder.App.Services;
using QuizBuilder.App.Theming;
using QuizBuilder.App.ViewModels;
using QuizBuilder.App.Views;
using QuizBuilder.Core;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Theming;

namespace QuizBuilder.App;

public partial class App : Application
{
    private ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();

        // Core: settings, document, .qbx, token protection, navigation.
        // Passing null puts settings.json beside the .exe (portable).
        services.AddQuizBuilderCore();

        // Shell.
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<ShellWindow>();

        // Tabs. Each View and its ViewModel are singletons so a tab keeps its
        // state across navigation; transient registration would rebuild the
        // tab (and lose scroll position and unsaved input) on every switch.
        services.AddSingleton<HelpViewModel>();
        services.AddSingleton<HelpView>();

        services.AddSingleton<ThemeViewModel>();
        services.AddSingleton<ThemeView>();

        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<SettingsView>();

        services.AddSingleton<QuizBuilderViewModel>();
        services.AddSingleton<QuizBuilderView>();

        services.AddSingleton<PreviewViewModel>();
        services.AddSingleton<PreviewView>();

        services.AddSingleton<PublishViewModel>();
        services.AddSingleton<GitHubViewModel>();
        services.AddSingleton<TakeViewModel>();
        services.AddSingleton<FlashCardsViewModel>();
        services.AddSingleton<StudyCardsViewModel>();
        services.AddSingleton<QuestionBankViewModel>();
        services.AddSingleton<PublishView>();
        services.AddSingleton<GitHubView>();
        services.AddSingleton<TakeView>();
        services.AddSingleton<FlashCardsView>();
        services.AddSingleton<StudyCardsView>();
        services.AddSingleton<QuestionBankView>();

        // Offline spell-check (feature B). Dictionary is a singleton: loading
        // the embedded en_US word list is not free, and one instance serves
        // every review. The ignore-list store and provider are cheap but kept
        // as singletons for consistency and so a future settings screen shares
        // one store with the review panel.
        services.AddSingleton<ISpellDictionary, HunspellDictionary>();
        services.AddSingleton<SpellIgnoreListStore>();
        services.AddSingleton<ITextReviewProvider, OfflineSpellProvider>();
        services.AddSingleton<SpellFixApplier>();

        // AI grammar review — local endpoint provider (phase 2). Reads the
        // endpoint/model from settings at call time via the closure, so a
        // settings change takes effect without rebuilding. Nothing invokes it
        // yet (the scope picker + accept/reject UI is phase 3); registering it
        // now keeps the wiring ready and startup unaffected.
        services.AddSingleton<IGrammarReviewProvider>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();
            return new LocalEndpointReviewProvider(
                () => (settings.Current.AiReview.LocalEndpointUrl, settings.Current.AiReview.Model));
        });

        _services = services.BuildServiceProvider();

        // Settings must load before the theme: the active theme id lives in
        // settings.json.
        var settings = _services.GetRequiredService<ISettingsService>();
        settings.Load();

        // Attempt history, same portable rule: a file beside the exe. Loaded
        // here so the Take tab can show a quiz's history the moment it opens
        // rather than hitting the disk on first paint.
        _services.GetRequiredService<IAttemptHistoryService>().Load();
        _services.GetRequiredService<IPausedAttemptService>().Load();
        _services.GetRequiredService<IQuestionBankService>().Load();

        // ThemeService reads settings in its constructor, so it must be
        // resolved after Load(). Subscribing here rather than inside the
        // service keeps Core free of any WPF reference.
        var themes = _services.GetRequiredService<IThemeService>();
        themes.ThemeChanged += (_, e) => ApplyTheme(e.Tokens);

        ApplyTheme(themes.Current);

        // Start the autosave timer from the restored settings.
        _services.GetRequiredService<IAutoSaveService>().Reconfigure();

        _services.GetRequiredService<ShellWindow>().Show();
    }

    /// <summary>
    /// Swaps the merged theme dictionary. Called at startup and on every
    /// ThemeChanged, which is what makes the Theme tab update live.
    /// </summary>
    public void ApplyTheme(ThemeTokens tokens)
    {
        var dictionary = ThemeResourceBuilder.Build(tokens);

        // Replace rather than append: merging repeatedly leaves stale
        // dictionaries in the chain, and lookup keeps finding the first match
        // rather than the newest.
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(dictionary);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Stop the timer before tearing down the container: a tick firing
        // mid-disposal would resolve services that are already gone.
        _services?.GetService<IAutoSaveService>()?.Stop();

        _services?.Dispose();
        base.OnExit(e);
    }
}
