using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.App.Services;

/// <summary>
/// The single <see cref="IGrammarReviewProvider"/> the app depends on. At call
/// time it reads the active <see cref="AiProvider"/> from settings and forwards
/// to the matching concrete provider (local endpoint or Claude). This keeps the
/// view models unaware of provider selection — they just call ReviewAsync — and
/// means switching providers in Settings takes effect on the next check with no
/// rebuild.
///
/// <para>
/// When the provider is Off, callers are expected to short-circuit before
/// reaching here (the grammar dialog does), but this also fails safe with a
/// clear message rather than picking a provider arbitrarily.
/// </para>
/// </summary>
public sealed class DispatchingGrammarProvider : IGrammarReviewProvider
{
    private readonly ISettingsService _settings;
    private readonly IGrammarReviewProvider _local;
    private readonly IGrammarReviewProvider _claude;

    public DispatchingGrammarProvider(
        ISettingsService settings,
        IGrammarReviewProvider local,
        IGrammarReviewProvider claude)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _local = local ?? throw new ArgumentNullException(nameof(local));
        _claude = claude ?? throw new ArgumentNullException(nameof(claude));
    }

    public string DisplayName => Active?.DisplayName ?? "AI grammar review (off)";

    private IGrammarReviewProvider? Active => _settings.Current.AiReview.Provider switch
    {
        AiProvider.LocalEndpoint => _local,
        AiProvider.Claude => _claude,
        _ => null, // Off
    };

    public Task<GrammarReviewResult> ReviewAsync(
        IReadOnlyList<GrammarField> fields,
        CancellationToken cancellationToken = default)
    {
        var active = Active;
        if (active is null)
            return Task.FromResult(GrammarReviewResult.Failed(
                "AI grammar review is off. Turn it on in Settings → AI grammar review."));

        return active.ReviewAsync(fields, cancellationToken);
    }
}
