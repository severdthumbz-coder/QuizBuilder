using System.Text.RegularExpressions;
using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.Core.Services;

/// <summary>Where in a field an issue was found, so the UI can highlight it and
/// a Replace can splice the correction at the right offset.</summary>
public sealed class TextOccurrence
{
    public TextOccurrence(TextField field, int start, int length)
    {
        Field = field;
        Start = start;
        Length = length;
    }

    /// <summary>The inventory field this occurrence lives in (carries its
    /// section/question ids and the read/write accessors).</summary>
    public TextField Field { get; }

    /// <summary>Character offset of the word within the field's text.</summary>
    public int Start { get; }

    public int Length { get; }
}

/// <summary>
/// One distinct misspelling, de-duplicated across the whole review, with every
/// place it occurs. The panel shows one row per issue ("somme (4)") and can
/// walk its <see cref="Occurrences"/> to highlight or bulk-replace.
/// </summary>
public sealed class TextIssue
{
    public TextIssue(string word, IReadOnlyList<string> suggestions, IReadOnlyList<TextOccurrence> occurrences)
    {
        Word = word;
        Suggestions = suggestions;
        Occurrences = occurrences;
    }

    /// <summary>The misspelled surface form, as first seen.</summary>
    public string Word { get; }

    /// <summary>Ordered replacement suggestions, best first; may be empty.</summary>
    public IReadOnlyList<string> Suggestions { get; }

    public IReadOnlyList<TextOccurrence> Occurrences { get; }

    public int Count => Occurrences.Count;
}

/// <summary>
/// The provider-agnostic spell-review pipeline: everything around the
/// dictionary lookup. Tokenizes each field, drops tokens that must never be
/// flagged (blank placeholders, numbers, alphanumerics, URLs, emails, short
/// acronyms, single letters), suppresses words on the user's ignore-list
/// (case-insensitive, whitespace-trimmed — the same normalization
/// <see cref="TakerKey"/> uses), asks the injected <see cref="ISpellDictionary"/>
/// about the rest, and collapses repeats into one <see cref="TextIssue"/> each.
///
/// <para>
/// Design proved in <c>tools/port/spell_review_port.py</c> before this was
/// written; the port caught a real tokenization bug ("mp3" leaving a stray
/// "mp") that the adjacency check below fixes. Pinned by
/// <c>SpellReviewEngineTests</c>. Pure Core: no Hunspell, no UI — the engine
/// itself is unit-tested with a fake dictionary.
/// </para>
/// </summary>
public sealed class SpellReviewEngine
{
    // A "word": a letter, then any run of word-chars/apostrophes/hyphens ending
    // in a letter. "don't" and "mother-in-law" stay single tokens; trailing
    // punctuation is excluded.
    private static readonly Regex WordRe =
        new(@"[^\W\d_](?:[\w'\-]*[^\W\d_])?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BlankTokenRe =
        new(@"\{\{\d+\}\}", RegexOptions.Compiled);
    private static readonly Regex UrlRe =
        new(@"(https?://|www\.)\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex EmailRe =
        new(@"\S+@\S+\.\S+", RegexOptions.Compiled);

    private readonly ISpellDictionary _dictionary;

    public SpellReviewEngine(ISpellDictionary dictionary)
    {
        _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
    }

    /// <summary>
    /// Reviews every field, returning de-duplicated issues in first-seen order.
    /// Words in <paramref name="ignoreWords"/> are treated as correct.
    /// </summary>
    public IReadOnlyList<TextIssue> Review(
        IReadOnlyList<TextField> fields,
        IEnumerable<string> ignoreWords)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var ignore = BuildIgnoreSet(ignoreWords);

        // Preserve first-seen order while accumulating occurrences per word.
        var order = new List<string>();
        var byKey = new Dictionary<string, Accumulator>(StringComparer.Ordinal);

        foreach (var field in fields)
        {
            var text = field.Text;
            if (string.IsNullOrEmpty(text))
                continue;

            foreach (var token in Tokenize(text))
            {
                if (IsExcluded(token.Text))
                    continue;

                var key = token.Text.ToLowerInvariant();

                if (ignore.Contains(key))
                    continue;

                if (_dictionary.IsKnown(token.Text))
                    continue;

                if (!byKey.TryGetValue(key, out var acc))
                {
                    acc = new Accumulator(token.Text, _dictionary.Suggest(token.Text));
                    byKey[key] = acc;
                    order.Add(key);
                }

                acc.Occurrences.Add(new TextOccurrence(field, token.Start, token.Length));
            }
        }

        var result = new List<TextIssue>(order.Count);
        foreach (var key in order)
        {
            var acc = byKey[key];
            result.Add(new TextIssue(acc.Word, acc.Suggestions, acc.Occurrences));
        }
        return result;
    }

    /// <summary>
    /// Normalizes an ignore-word the way <see cref="TakerKey"/> normalizes an
    /// email: trim, then lower-invariant, so "Photosynthesis", "photosynthesis "
    /// and "photosynthesis" collapse to one ignored form.
    /// </summary>
    public static string NormalizeIgnoreWord(string word) =>
        (word ?? string.Empty).Trim().ToLowerInvariant();

    private static HashSet<string> BuildIgnoreSet(IEnumerable<string>? words)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (words is null) return set;
        foreach (var w in words)
        {
            if (string.IsNullOrWhiteSpace(w)) continue;
            set.Add(NormalizeIgnoreWord(w));
        }
        return set;
    }

    private readonly record struct WordToken(string Text, int Start, int Length);

    /// <summary>
    /// Splits text into word tokens with spans. Blank placeholders, URLs and
    /// emails are masked to spaces first (preserving offsets) so their internal
    /// letters never surface; a word fragment directly abutting a digit in the
    /// original text (the "mp" of "mp3") is dropped as part of an alphanumeric
    /// run.
    /// </summary>
    private static IEnumerable<WordToken> Tokenize(string text)
    {
        var masked = text.ToCharArray();
        MaskMatches(masked, text, BlankTokenRe);
        MaskMatches(masked, text, UrlRe);
        MaskMatches(masked, text, EmailRe);
        var maskedText = new string(masked);

        foreach (Match m in WordRe.Matches(maskedText))
        {
            char before = m.Index > 0 ? text[m.Index - 1] : '\0';
            char after = m.Index + m.Length < text.Length ? text[m.Index + m.Length] : '\0';
            if (char.IsDigit(before) || char.IsDigit(after))
                continue;

            yield return new WordToken(m.Value, m.Index, m.Length);
        }
    }

    private static void MaskMatches(char[] buffer, string source, Regex rx)
    {
        foreach (Match m in rx.Matches(source))
        {
            for (int i = m.Index; i < m.Index + m.Length; i++)
                buffer[i] = ' ';
        }
    }

    /// <summary>
    /// Tokens never flagged regardless of the dictionary: single characters,
    /// short ALL-CAPS acronyms (NASA, HTTP), and anything containing a digit
    /// (h2o, mp3). Kept in step with the port's <c>is_excluded_token</c>.
    /// </summary>
    private static bool IsExcluded(string word)
    {
        if (word.Length <= 1)
            return true;

        if (word.Length <= 5 && IsAllUpper(word))
            return true;

        foreach (var ch in word)
            if (char.IsDigit(ch))
                return true;

        return false;
    }

    private static bool IsAllUpper(string word)
    {
        foreach (var ch in word)
            if (char.IsLetter(ch) && !char.IsUpper(ch))
                return false;
        return true;
    }

    private sealed class Accumulator
    {
        public Accumulator(string word, IReadOnlyList<string> suggestions)
        {
            Word = word;
            Suggestions = suggestions;
        }

        public string Word { get; }
        public IReadOnlyList<string> Suggestions { get; }
        public List<TextOccurrence> Occurrences { get; } = new();
    }
}
