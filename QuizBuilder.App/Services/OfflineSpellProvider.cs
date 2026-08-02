using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;

namespace QuizBuilder.App.Services;

/// <summary>
/// The offline spelling reviewer: the first <see cref="ITextReviewProvider"/>.
/// Composes Core's <see cref="SpellReviewEngine"/> (tokenization, exclusions,
/// de-dup — all tested) with the App-only <see cref="HunspellDictionary"/> and
/// the persisted <see cref="SpellIgnoreListStore"/>. A future AI grammar
/// provider is a second implementation of the same interface, selected in
/// settings; the review UI depends only on <see cref="ITextReviewProvider"/>.
/// </summary>
public sealed class OfflineSpellProvider : ITextReviewProvider
{
    private readonly SpellReviewEngine _engine;
    private readonly SpellIgnoreListStore _ignoreList;

    public OfflineSpellProvider(ISpellDictionary dictionary, SpellIgnoreListStore ignoreList)
    {
        ArgumentNullException.ThrowIfNull(dictionary);
        _engine = new SpellReviewEngine(dictionary);
        _ignoreList = ignoreList ?? throw new ArgumentNullException(nameof(ignoreList));
    }

    public string DisplayName => "Spelling (offline)";

    public IReadOnlyList<TextIssue> Review(IReadOnlyList<TextField> fields) =>
        _engine.Review(fields, _ignoreList.GetWords());
}
