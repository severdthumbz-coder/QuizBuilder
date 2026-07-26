using System.Text.RegularExpressions;
using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Keeps a fill-in-the-blank question's <see cref="Blank"/> list in step with
/// the {{n}} tokens in its prompt.
///
/// The prompt is the source of truth for WHICH blanks exist: the user types
/// the text, so the text decides. Answers are preserved by ordinal, never by
/// position -- editing "The {{1}} sat on the {{2}}" into "The {{2}} held the
/// {{1}}" must keep each answer bound to its own token, not swap them.
///
/// Malformed input (duplicate tokens, gaps, no tokens at all) produces a
/// warning and a best-effort result rather than an exception. Someone is typing
/// mid-sentence; the model should not throw because the prompt is briefly
/// nonsense.
/// </summary>
public static partial class BlankSynchroniser
{
    [GeneratedRegex(@"\{\{(\d+)\}\}")]
    private static partial Regex TokenRegex();

    public sealed record SyncResult(List<Blank> Blanks, IReadOnlyList<string> Warnings);

    /// <summary>Ordinals of the {{n}} tokens, in order of appearance.</summary>
    public static List<int> TokensIn(string? prompt)
    {
        var result = new List<int>();
        if (string.IsNullOrEmpty(prompt)) return result;

        foreach (Match m in TokenRegex().Matches(prompt))
        {
            // int.Parse is safe: the pattern only matches digits. A prompt with
            // {{99999999999}} would overflow, so TryParse keeps it graceful.
            if (int.TryParse(m.Groups[1].Value, out var ordinal))
                result.Add(ordinal);
        }

        return result;
    }

    public static SyncResult Sync(string? prompt, IEnumerable<Blank> existing)
    {
        var warnings = new List<string>();
        var current = existing?.ToList() ?? new List<Blank>();
        var found = TokensIn(prompt);

        if (found.Count == 0)
        {
            if (current.Count > 0)
                warnings.Add("The prompt has no {{1}} style tokens, so the answers below are not reachable.");

            return new SyncResult(new List<Blank>(), warnings);
        }

        var duplicates = found.GroupBy(o => o)
                              .Where(g => g.Count() > 1)
                              .Select(g => g.Key)
                              .OrderBy(o => o)
                              .ToList();

        if (duplicates.Count > 0)
        {
            warnings.Add($"Token {string.Join(", ", duplicates.Select(d => $"{{{{{d}}}}}"))} "
                         + "appears more than once. Each blank is answered once.");
        }

        var unique = found.Distinct().OrderBy(o => o).ToList();

        var expected = Enumerable.Range(1, unique.Count).ToList();
        if (!unique.SequenceEqual(expected))
        {
            warnings.Add($"Tokens run {string.Join(", ", unique)}. Numbering from 1 without gaps "
                         + "is easier to follow, though it will still work.");
        }

        var byOrdinal = current
            .GroupBy(b => b.Ordinal)
            .ToDictionary(g => g.Key, g => g.First());

        var result = new List<Blank>();
        foreach (var ordinal in unique)
        {
            result.Add(byOrdinal.TryGetValue(ordinal, out var existingBlank)
                ? existingBlank
                : new Blank { Ordinal = ordinal });
        }

        var orphaned = byOrdinal.Keys.Where(o => !unique.Contains(o)).OrderBy(o => o).ToList();
        if (orphaned.Count > 0)
        {
            warnings.Add($"Removed the answers for {string.Join(", ", orphaned.Select(o => $"{{{{{o}}}}}"))} "
                         + "because that token is no longer in the prompt.");
        }

        return new SyncResult(result, warnings);
    }

    /// <summary>
    /// Appends the next free token to a prompt. Used by the "Add blank" button
    /// so the user does not have to remember the syntax.
    /// </summary>
    public static string AppendNextToken(string? prompt)
    {
        var used = TokensIn(prompt);
        var next = used.Count == 0 ? 1 : used.Max() + 1;

        var text = prompt ?? string.Empty;
        var separator = text.Length > 0 && !text.EndsWith(' ') ? " " : string.Empty;

        return $"{text}{separator}{{{{{next}}}}}";
    }
}
