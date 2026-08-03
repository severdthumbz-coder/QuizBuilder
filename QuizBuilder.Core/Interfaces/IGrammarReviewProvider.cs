namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// One proposed grammar/phrasing correction: a located span of a field's text
/// and the rewrite for it, with a short reason. Distinct from a spelling
/// <c>TextIssue</c> — grammar operates on phrases, carries an explanation, and
/// comes from an async model call — so it is deliberately a separate type and
/// leaves the working offline spell-checker untouched.
/// </summary>
public sealed class GrammarSuggestion
{
    public GrammarSuggestion(
        int fieldId, int start, int length,
        string original, string rewrite, string explanation)
    {
        FieldId = fieldId;
        Start = start;
        Length = length;
        Original = original;
        Rewrite = rewrite;
        Explanation = explanation;
    }

    /// <summary>Id of the field this applies to (assigned when the batch is
    /// built), so an accepted rewrite can be routed back to the right field.</summary>
    public int FieldId { get; }

    /// <summary>Offset of the original span within the field's (HTML-stripped) text.</summary>
    public int Start { get; }

    public int Length { get; }

    /// <summary>The exact source substring being replaced (as found in the field).</summary>
    public string Original { get; }

    /// <summary>The proposed replacement.</summary>
    public string Rewrite { get; }

    /// <summary>A short reason, shown so the reviewer can judge before accepting.</summary>
    public string Explanation { get; }
}

/// <summary>
/// Outcome of an AI grammar review: either success with zero-or-more anchored
/// suggestions, or a failure carrying a plain-words message. Mirrors the
/// GitHubResult convention (Ok/Failed factories) so callers handle it the same
/// way. An empty suggestion list on success means "nothing to change", which is
/// NOT an error.
/// </summary>
public sealed class GrammarReviewResult
{
    private GrammarReviewResult(bool success, IReadOnlyList<GrammarSuggestion> suggestions, string? message)
    {
        Success = success;
        Suggestions = suggestions;
        Message = message;
    }

    public bool Success { get; }

    public IReadOnlyList<GrammarSuggestion> Suggestions { get; }

    /// <summary>Plain-words error when <see cref="Success"/> is false; null otherwise.</summary>
    public string? Message { get; }

    public static GrammarReviewResult Ok(IReadOnlyList<GrammarSuggestion> suggestions) =>
        new(true, suggestions, null);

    public static GrammarReviewResult Failed(string message) =>
        new(false, Array.Empty<GrammarSuggestion>(), message);
}

/// <summary>
/// One checkable field handed to the grammar reviewer: its assigned id, a label
/// for context in the prompt, and its already-HTML-stripped text. The caller
/// (App) builds these from the inventory, stripping the description via
/// DescriptionParser exactly as the spell-checker does, so markup never reaches
/// the model.
/// </summary>
public sealed class GrammarField
{
    public GrammarField(int fieldId, string label, string text)
    {
        FieldId = fieldId;
        Label = label;
        Text = text;
    }

    public int FieldId { get; }
    public string Label { get; }
    public string Text { get; }
}

/// <summary>
/// An opt-in AI grammar reviewer. Async because it makes a network call (unlike
/// the synchronous offline spell-checker). Implementations: a local
/// OpenAI-compatible endpoint (built first), and Claude. Both reuse the same
/// Core prompt-builder and response-parser; only the transport/auth differs.
/// </summary>
public interface IGrammarReviewProvider
{
    /// <summary>A short label, e.g. "Local endpoint" or "Claude".</summary>
    string DisplayName { get; }

    /// <summary>
    /// Reviews the supplied fields and returns anchored suggestions, or a
    /// failure result. Never throws for expected conditions (no network, bad
    /// key, malformed reply) — those come back as <see cref="GrammarReviewResult.Failed"/>.
    /// </summary>
    Task<GrammarReviewResult> ReviewAsync(
        IReadOnlyList<GrammarField> fields,
        CancellationToken cancellationToken = default);
}
