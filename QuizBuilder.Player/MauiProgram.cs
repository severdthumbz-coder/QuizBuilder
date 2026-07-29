using Microsoft.Extensions.Logging;
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
        builder.Services.AddSingleton<IQbxImporter, QbxImporter>();
        builder.Services.AddSingleton<IResultsEmailService, ResultsEmailService>();

        // --- View models (transient: a fresh VM per navigation) ---
        builder.Services.AddTransient<IdentityViewModel>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<TakeViewModel>();
        builder.Services.AddTransient<ResultsViewModel>();

        // --- Pages (transient, resolved with their VM injected) ---
        builder.Services.AddTransient<IdentityPage>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<TakePage>();
        builder.Services.AddTransient<ResultsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
