using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// SpellReviewEngine: the provider-agnostic pipeline around the dictionary
/// lookup. C# port of tools/port/spell_review_port.py. The Hunspell engine
/// itself is App-only and confirmed by the Windows build; everything here — 
/// tokenization spans, exclusions, ignore-list normalization, de-dup — runs on
/// a plain runner against a fake dictionary.
/// </summary>
public class SpellReviewEngineTests
{
    // ----- a fake dictionary standing in for Hunspell ---------------------- //

    private sealed class FakeDictionary : ISpellDictionary
    {
        private readonly HashSet<string> _known;
        public FakeDictionary(IEnumerable<string> known) =>
            _known = new HashSet<string>(known, StringComparer.OrdinalIgnoreCase);

        public bool IsKnown(string word) => _known.Contains(word);

        public IReadOnlyList<string> Suggest(string word)
        {
            // Enough to prove suggestions flow through: known words sharing the
            // first letter, shortest first. Not a quality test.
            var first = char.ToLowerInvariant(word[0]);
            return _known
                .Where(k => k.Length > 0 && char.ToLowerInvariant(k[0]) == first)
                .OrderBy(k => Math.Abs(k.Length - word.Length))
                .Take(3)
                .ToList();
        }
    }

    private static readonly string[] CommonKnown =
    {
        "the", "quick", "brown", "fox", "a", "some", "colour", "color",
        "text", "with", "and", "more", "please", "fill", "in", "see", "or",
        "mail", "now", "sent", "rockets", "files",
    };

    private static SpellReviewEngine Engine(params string[] known) =>
        new(new FakeDictionary(known.Length == 0 ? CommonKnown : known));

    // ----- TextField helpers ---------------------------------------------- //

    /// <summary>A standalone TextField wrapping a mutable string cell, so tests
    /// don't need a whole QuizDocument to exercise the engine.</summary>
    private static TextField Field(string id, string text)
    {
        var box = new string[] { text };
        return new TextField(
            TextFieldKind.QuestionPrompt, id, sectionId: null, questionId: null,
            get: () => box[0], set: v => box[0] = v);
    }

    private static IReadOnlyList<TextField> Fields(params (string id, string text)[] items) =>
        items.Select(i => Field(i.id, i.text)).ToList();

    /// <summary>A description field (HTML-bearing), for the markup-stripping tests.</summary>
    private static TextField DescriptionField(string text)
    {
        var box = new string[] { text };
        return new TextField(
            TextFieldKind.QuizDescription, "Quiz description", null, null,
            get: () => box[0], set: v => box[0] = v);
    }

    // ----- description markup stripping ------------------------------------ //

    [Fact]
    public void DescriptionTagNamesAreNotFlagged()
    {
        // The dictionary knows the real words but NOT the tag names; if the
        // stripping failed, "strong"/"br"/"ul"/"li" would be flagged.
        var engine = Engine("rules", "and", "regulations", "bold", "italic", "bullets");
        var issues = engine.Review(
            new[] { DescriptionField(
                "<strong>Rules and Regulations</strong><br><br><ul><li>bold italic bullets</li></ul>") },
            ignoreWords: Array.Empty<string>());

        var flagged = issues.Select(i => i.Word.ToLowerInvariant()).ToList();
        Assert.DoesNotContain("strong", flagged);
        Assert.DoesNotContain("br", flagged);
        Assert.DoesNotContain("ul", flagged);
        Assert.DoesNotContain("li", flagged);
    }

    [Fact]
    public void DescriptionRealMisspellingStillFlaggedButNotReplaceable()
    {
        // "regulatons" is misspelled; it should be caught, but marked
        // non-replaceable because offsets are on the stripped text.
        var engine = Engine("rules", "and");
        var issues = engine.Review(
            new[] { DescriptionField("<strong>Rules and regulatons</strong>") },
            ignoreWords: Array.Empty<string>());

        var issue = Assert.Single(issues.Where(i => i.Word.Equals("regulatons", StringComparison.OrdinalIgnoreCase)));
        Assert.All(issue.Occurrences, o => Assert.False(o.Replaceable));
    }

    [Fact]
    public void NonDescriptionOccurrencesAreReplaceable()
    {
        var issues = Engine().Review(
            Fields(("f1", "somme")), ignoreWords: Array.Empty<string>());
        Assert.All(Assert.Single(issues).Occurrences, o => Assert.True(o.Replaceable));
    }

    // ----- exclusions ----------------------------------------------------- //

    [Fact]
    public void BlankPlaceholdersAreNeverFlagged()
    {
        var issues = Engine().Review(
            Fields(("f1", "fill in {{1}} and {{2}} please")),
            ignoreWords: Array.Empty<string>());
        // {{1}}/{{2}} masked; all remaining words are known -> no issues, and
        // crucially no "1"/"2" or brace fragments surface.
        Assert.Empty(issues);
    }

    [Fact]
    public void UrlsAndEmailsAreMaskedWhole()
    {
        var issues = Engine().Review(
            Fields(("f1", "see https://example.com/x or mail a@b.com now")),
            ignoreWords: Array.Empty<string>());
        // "example", "com", "b" etc. must not appear as flagged words.
        Assert.DoesNotContain(issues, i => i.Word is "https" or "example" or "com" or "b");
        Assert.Empty(issues); // remaining words (see/or/mail/now) are all known
    }

    [Fact]
    public void ShortAcronymsAndAlphanumericsAreSkipped()
    {
        // NASA (short caps), h2o and mp3 (contain/adjoin digits) never flagged,
        // even though the fake dict doesn't know them.
        var issues = Engine("sent", "rockets", "and", "files").Review(
            Fields(("f1", "NASA sent 3 rockets and h2o mp3 files")),
            ignoreWords: Array.Empty<string>());
        Assert.Empty(issues);
    }

    // ----- ignore-list ----------------------------------------------------- //

    [Fact]
    public void IgnoreListIsCaseAndWhitespaceInsensitive()
    {
        var fields = Fields(("f1", "Photosynthesis in the Mitochondria"));
        var dict = new[] { "the", "in" }; // deliberately does NOT know the bio terms
        var engine = Engine(dict);

        var flaggedNoIgnore = engine.Review(fields, Array.Empty<string>())
            .Select(i => i.Word.ToLowerInvariant()).OrderBy(w => w).ToList();
        Assert.Equal(new[] { "mitochondria", "photosynthesis" }, flaggedNoIgnore);

        var flaggedWithIgnore = engine.Review(
            fields, new[] { "  PHOTOSYNTHESIS ", "Mitochondria" });
        Assert.Empty(flaggedWithIgnore);
    }

    [Fact]
    public void NormalizeIgnoreWordTrimsAndLowercases() =>
        Assert.Equal("photosynthesis", SpellReviewEngine.NormalizeIgnoreWord("  PhotoSynthesis "));

    // ----- de-dup ---------------------------------------------------------- //

    [Fact]
    public void RepeatedMisspellingCollapsesToOneIssueWithEveryOccurrence()
    {
        var issues = Engine().Review(
            Fields(("f1", "somme text with somme"), ("f2", "and somme more")),
            ignoreWords: Array.Empty<string>());

        var issue = Assert.Single(issues);
        Assert.Equal("somme", issue.Word);
        Assert.Equal(3, issue.Count);

        var placed = issue.Occurrences
            .Select(o => ((string)o.Field.Label, o.Start)).ToList();
        Assert.Equal(new[] { ("f1", 0), ("f1", 16), ("f2", 4) }, placed);
    }

    // ----- clean / suggestions / edges ------------------------------------ //

    [Fact]
    public void AllKnownWordsProduceNoIssues() =>
        Assert.Empty(Engine().Review(
            Fields(("f1", "the quick brown fox")), Array.Empty<string>()));

    [Fact]
    public void UnknownWordCarriesSuggestions()
    {
        // "color" present in CommonKnown, so drop it to force a miss.
        var known = CommonKnown.Where(w => w != "color").ToArray();
        var issues = Engine(known).Review(
            Fields(("f1", "colour and color")), Array.Empty<string>());

        var issue = Assert.Single(issues);
        Assert.Equal("color", issue.Word);
        Assert.NotEmpty(issue.Suggestions);
        Assert.All(issue.Suggestions, s => Assert.Equal('c', char.ToLowerInvariant(s[0])));
    }

    [Fact]
    public void OccurrenceSpansMapBackToSourceOffsets()
    {
        var known = CommonKnown.Where(w => w != "quick").ToArray();
        var issues = Engine(known).Review(
            Fields(("f1", "the quick fox")), Array.Empty<string>());

        var occ = Assert.Single(Assert.Single(issues).Occurrences);
        Assert.Equal(4, occ.Start);   // "quick" starts at index 4
        Assert.Equal(5, occ.Length);
    }

    [Fact]
    public void ReplaceViaOccurrenceRoundTripsToField()
    {
        // The whole point: an issue's occurrence carries the live field, so a
        // correction can be written back. (The App routes this through
        // IQuizDocumentService; here we prove the accessor reaches the source.)
        var known = CommonKnown.Where(w => w != "colour").ToArray();
        var fields = Fields(("f1", "colour"));
        var issue = Assert.Single(Engine(known).Review(fields, Array.Empty<string>()));
        var occ = Assert.Single(issue.Occurrences);

        occ.Field.Set("color");
        Assert.Equal("color", occ.Field.Text);
    }

    [Fact]
    public void EmptyAndWhitespaceFieldsYieldNothing() =>
        Assert.Empty(Engine().Review(
            Fields(("f1", ""), ("f2", "   ")), Array.Empty<string>()));

    [Fact]
    public void NullFieldsThrows() =>
        Assert.Throws<ArgumentNullException>(() =>
            Engine().Review(null!, Array.Empty<string>()));

    [Fact]
    public void NullDictionaryThrows() =>
        Assert.Throws<ArgumentNullException>(() => new SpellReviewEngine(null!));
}
