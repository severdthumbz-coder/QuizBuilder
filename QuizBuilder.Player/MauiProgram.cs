using Microsoft.Extensions.Logging;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;
using QuizBuilder.Player.Services;
using QuizBuilder.Player.ViewModels;
using QuizBuilder.Player.Views;

namespace QuizBuilder.Player;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                // One OpenSans variable font covers regular and bold weights;
                // hierarchy is expressed via FontAttributes in the styles rather
                // than a second physical file. Registered under both aliases so
                // any "OpenSansSemibold" reference still resolves to it.
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansSemibold");
            });

        // --- Player services ---
        // The session is the shared spine every screen reads, so it is a
        // singleton. The importer and email services are stateless helpers.
        builder.Services.AddSingleton<QuizSessionService>();
        builder.Services.AddSingleton<QuizLibraryService>();
        builder.Services.AddSingleton<IQbxImporter, QbxImporter>();
        builder.Services.AddSingleton<IResultsEmailService, ResultsEmailService>();

        // Core's attempt history, pointed at the app sandbox. On desktop this
        // service writes history.json "beside the exe"; a phone has no such
        // place, so we pass FileSystem.AppDataDirectory through the service's
        // overrideDirectory seam -- the storage-path adaptation the HANDOFF
        // calls out. Singleton so its in-memory list is shared by the session
        // (which appends on submit) and the history screen (which reads it).
        builder.Services.AddSingleton<IAttemptHistoryService>(
            _ => new AttemptHistoryService(FileSystem.AppDataDirectory));

        // Paused sittings, same sandbox-path adaptation and singleton rationale
        // as history: the session saves/reads it and the Home screen lists it.
        builder.Services.AddSingleton<IPausedAttemptService>(
            _ => new PausedAttemptService(FileSystem.AppDataDirectory));

        // --- View models (transient: a fresh VM per navigation) ---
        builder.Services.AddTransient<IdentityViewModel>();
        builder.Services.AddTransient<LibraryViewModel>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<TakeViewModel>();
        builder.Services.AddTransient<ResultsViewModel>();
        builder.Services.AddTransient<StudyCardsViewModel>();
        builder.Services.AddTransient<HistoryViewModel>();
        builder.Services.AddTransient<AttemptDetailViewModel>();

        // --- Pages (transient, resolved with their VM injected) ---
        builder.Services.AddTransient<IdentityPage>();
        builder.Services.AddTransient<LibraryPage>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<TakePage>();
        builder.Services.AddTransient<ResultsPage>();
        builder.Services.AddTransient<StudyCardsPage>();
        builder.Services.AddTransient<HistoryPage>();
        builder.Services.AddTransient<AttemptDetailPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        var app = builder.Build();

        // Read history.json off disk once, now, so the first results screen and
        // the history screen see prior sittings. The service swallows a missing
        // or corrupt file (first run, or a bad write) and starts empty rather
        // than throwing here, where a throw would abort app startup.
        app.Services.GetRequiredService<IAttemptHistoryService>().Load();
        app.Services.GetRequiredService<IPausedAttemptService>().Load();
        app.Services.GetRequiredService<QuizLibraryService>().Load();

        return app;
    }
}
