using QuizBuilder.Core.Services;

namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// A source of spelling knowledge: can it recognise a word, and what does it
/// suggest for one it cannot? This is the seam that keeps the review pipeline
/// testable without a real dictionary engine.
///
/// <para>
/// The offline implementation (App layer) wraps Hunspell. A test passes a fake
/// backed by a small word set. Core's <see cref="SpellReviewEngine"/> depends
/// only on this interface, never on Hunspell, so all of the pipeline logic
/// around the lookup — tokenization, exclusions, the ignore-list, de-duping —
/// is exercised on a plain CI runner.
/// </para>
/// </summary>
public interface ISpellDictionary
{
    /// <summary>True if <paramref name="word"/> is a correctly-spelled word.</summary>
    bool IsKnown(string word);

    /// <summary>
    /// Ordered replacement suggestions for a misspelled word, best first.
    /// May be empty when the engine has nothing to offer.
    /// </summary>
    IReadOnlyList<string> Suggest(string word);
}

/// <summary>
/// Reviews the authored text of a quiz and returns the issues found. The
/// offline spell provider is the first implementation; a future opt-in AI
/// grammar provider is a second, selected in settings. Both consume the same
/// <see cref="TextField"/> inventory and return the same
/// <see cref="TextIssue"/> shape, so the review UI is provider-agnostic.
///
/// <para>
/// The pass is one-shot (button-triggered, whole-quiz or section-scoped), not
/// incremental: callers hand over a batch of fields and get every issue back at
/// once. This matches the deliberate "check when ready" UX rather than live
/// as-you-type checking, which would be noisy on quiz content full of proper
/// nouns and domain terms.
/// </para>
/// </summary>
public interface ITextReviewProvider
{
    /// <summary>A short label for the settings/menu, e.g. "Spelling (offline)".</summary>
    string DisplayName { get; }

    /// <summary>
    /// Reviews the supplied fields and returns de-duplicated issues in
    /// first-seen order, each carrying every occurrence across the batch.
    /// </summary>
    IReadOnlyList<TextIssue> Review(IReadOnlyList<TextField> fields);
}
