using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using OnboardingKit;
using QuizBuilder.App.Onboarding;
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

        // Desktop glue for Core's crash-recovery service (snapshot while dirty,
        // clear on clean save/exit, restore prompt at startup).
        services.AddSingleton<CrashRecoveryCoordinator>();

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
        services.AddSingleton<ReviewViewModel>();
        services.AddSingleton<QuestionBankViewModel>();
        services.AddSingleton<PublishView>();
        services.AddSingleton<GitHubView>();
        services.AddSingleton<TakeView>();
        services.AddSingleton<FlashCardsView>();
        services.AddSingleton<StudyCardsView>();
        services.AddSingleton<ReviewView>();
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

        // AI grammar review. The app depends on a single IGrammarReviewProvider
        // (the dispatcher), which reads the active AiProvider from settings at
        // call time and forwards to the local-endpoint or Claude transport. Both
        // concrete providers read their config (endpoint/model, or key/model)
        // from settings via closures, so a settings change needs no rebuild.
        services.AddSingleton<IGrammarReviewProvider>(sp =>
        {
            var settings = sp.GetRequiredService<ISettingsService>();

            var local = new LocalEndpointReviewProvider(
                () => (settings.Current.AiReview.LocalEndpointUrl, settings.Current.AiReview.Model));

            var claude = new ClaudeReviewProvider(
                () => (settings.GetAiReviewKey(), settings.Current.AiReview.Model));

            return new DispatchingGrammarProvider(settings, local, claude);
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

        // Onboarding: wire the kit to our settings and navigation. The store
        // reads/writes the two flags on the live AppSettings and saves through
        // SettingsService, so the kit never references Core directly. The tab
        // selector maps a tour step's integer TabIndex (a NavDestination value)
        // back to a navigation call. Done after settings load so the flags are
        // real; the first-run auto-start itself is scheduled in ShellWindow once
        // the window has drawn.
        var navigation = _services.GetRequiredService<INavigationService>();
        var onboardingStore = new DelegateOnboardingStore(
            () => settings.Current.Onboarding.ShowOnboardingHelpers,
            v => settings.Current.Onboarding.ShowOnboardingHelpers = v,
            () => settings.Current.Onboarding.HasSeenTour,
            v => settings.Current.Onboarding.HasSeenTour = v,
            () => settings.Save());
        OnboardingController.Current.Initialize(
            onboardingStore,
            OnboardingContent.TourSteps,
            OnboardingContent.LegendItems,
            selectTab: i => navigation.NavigateTo((NavDestination)i));

        _services.GetRequiredService<ShellWindow>().Show();

        // Crash recovery. Check for a snapshot left by a previous session that
        // did not close cleanly, and offer to restore it — after the window is
        // shown so the prompt has an owner. Then start watching the document so
        // new edits (including a just-restored one) are snapshotted. Order
        // matters: the prompt runs before Start(), and Start() re-captures if
        // the restore left the document dirty, keeping a crash-after-restore
        // covered.
        var crashRecovery = _services.GetRequiredService<CrashRecoveryCoordinator>();
        crashRecovery.CheckForRecovery();
        crashRecovery.Start();
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

        // Clean exit: delete the recovery snapshot, so its presence at the next
        // launch unambiguously means a crash. Only reached on a normal shutdown;
        // a crash never runs OnExit, which is exactly why the snapshot survives.
        _services?.GetService<CrashRecoveryCoordinator>()?.ClearForCleanExit();

        _services?.Dispose();
        base.OnExit(e);
    }
}
