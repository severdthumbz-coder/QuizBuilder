using Microsoft.Extensions.DependencyInjection;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;

namespace QuizBuilder.Core;

/// <summary>
/// Registers the Core services. The App project calls this, then adds its own
/// UI-layer registrations on top. Keeping Core's registrations here rather
/// than in App means the test project can spin up the same graph.
/// </summary>
public static class CoreServiceRegistration
{
    /// <param name="settingsDirectory">
    /// Overrides where settings.json lives. Production passes null, so it
    /// lands beside the .exe. Tests pass a temp directory to avoid writing
    /// into the test runner's output folder.
    /// </param>
    public static IServiceCollection AddQuizBuilderCore(
        this IServiceCollection services,
        string? settingsDirectory = null)
    {
        // Singleton: TokenProtector holds the unlocked session key. A second
        // instance would report itself locked and silently fail to read a
        // token the first one had already unlocked.
        services.AddSingleton<TokenProtector>();
        services.AddSingleton<ITokenProtector>(sp => sp.GetRequiredService<TokenProtector>());

        // Registered against the concrete type as well, because SettingsService
        // needs TokenProtector's SetPendingCipherText, which is deliberately
        // not on the ITokenProtector interface (it is an implementation
        // detail of the load sequence, not something tabs should call).
        services.AddSingleton<ISettingsService>(sp =>
            new SettingsService(sp.GetRequiredService<TokenProtector>(), settingsDirectory));

        // Singleton: this IS the shared document all tabs read. A transient
        // registration would hand each tab its own empty quiz.
        services.AddSingleton<IQuizDocumentService, QuizDocumentService>();

        // Singleton: one history for the one shared document. A second
        // instance would hold snapshots of edits the first never saw, so
        // undoing would jump the document to an arrangement that never
        // existed. Subscribes to DocumentChanged in its constructor, so it
        // must be resolved for the subscription to exist at all.
        services.AddSingleton<IUndoService, UndoService>();

        // Singleton: holds the in-memory image working set across saves.
        services.AddSingleton<IQuizPackageService, QuizPackageService>();

        // Singleton: owns which tab is active. Two instances would let the rail
        // and the content host disagree about where the user is.
        services.AddSingleton<INavigationService, NavigationService>();

        // Singleton: owns the active theme. Must be resolved AFTER
        // ISettingsService.Load(), since its constructor reads the saved
        // theme id and custom tokens.
        services.AddSingleton<IThemeService, ThemeService>();

        // Singleton: owns one timer for the whole app. A second instance would
        // mean two timers writing the same .qbx file concurrently.
        services.AddSingleton<IAutoSaveService, AutoSaveService>();
        services.AddSingleton<IQuizCompiler, QuizCompiler>();
        services.AddSingleton<IHtmlExporter, HtmlExporter>();
        services.AddSingleton<IWordExporter, WordExporter>();
        services.AddSingleton<IExcelExporter, ExcelExporter>();
        services.AddSingleton<IExcelImporter, ExcelImporter>();

        // Singleton so the HttpClient inside is reused rather than one socket
        // per publish -- the classic HttpClient-per-call socket exhaustion.
        services.AddSingleton<IGitHubService, GitHubService>();
        services.AddSingleton<IQuizGrader, QuizGrader>();
        services.AddSingleton<IAttemptHistoryService, AttemptHistoryService>();
        services.AddSingleton<IPausedAttemptService, PausedAttemptService>();
        services.AddSingleton<IQuestionBankService, QuestionBankService>();
        services.AddSingleton<IQuizWebExporter, QuizWebExporter>();

        return services;
    }
}
