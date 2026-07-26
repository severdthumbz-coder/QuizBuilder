using System.IO.Compression;
using System.Xml.Linq;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using QuizBuilder.Core.Theming;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Everything about a sequence question that lives OUTSIDE the grader: the
/// compiler's presentation order (the shuffle the taker sees, kept separate
/// from the answer key) and the way each exporter and the importer carry it.
///
/// <para>
/// The presentation order is the one genuinely new piece of behaviour. Like a
/// matching question's shuffled right-hand column, it is a projection on the
/// compiled question -- the model's Items stay in correct order because they
/// are the answer key. The rule the taker must never be handed: for two or more
/// items with randomisation on, the presentation is never the identity, or the
/// items would already be in order on screen.
/// </para>
/// </summary>
public class SequencePresentationTests
{
    private static readonly ThemeTokens Theme = BuiltInThemes.Academic();

    private static SequenceQuestion Question(int itemCount, double points = 1)
    {
        var q = new SequenceQuestion { Prompt = "Order these", Points = points };
        for (var i = 0; i < itemCount; i++) q.Items.Add($"Item {i}");
        return q;
    }

    private static CompiledQuiz Compile(QuizSettings settings, int seed, params Question[] questions)
    {
        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "S" };
        foreach (var q in questions) section.Questions.Add(q);
        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);

        return new QuizCompiler().Compile(doc, settings, seed);
    }

    private static CompiledQuestion CompileOne(QuizSettings settings, int seed, SequenceQuestion q)
        => Compile(settings, seed, q).Sections.SelectMany(s => s.Questions).Single();

    private static bool IsIdentity(IReadOnlyList<int> order)
    {
        for (var i = 0; i < order.Count; i++)
            if (order[i] != i) return false;
        return true;
    }

    // --- Presentation order --------------------------------------------------

    [Fact]
    public void PresentationIsAPermutationOfTheItemIndices()
    {
        var compiled = CompileOne(new QuizSettings { RandomizeAnswerOrder = true }, seed: 3, Question(6));

        Assert.NotNull(compiled.SequencePresentation);
        Assert.Equal(
            new[] { 0, 1, 2, 3, 4, 5 },
            compiled.SequencePresentation!.OrderBy(i => i).ToArray());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void RandomisedPresentationIsNeverTheCorrectOrder(int itemCount)
    {
        // Across many seeds the taker must never open the question with the
        // items already in order. The rotate-by-one fallback in the compiler
        // exists precisely for the seeds where the shuffle lands on identity.
        for (var seed = 0; seed < 200; seed++)
        {
            var compiled = CompileOne(
                new QuizSettings { RandomizeAnswerOrder = true }, seed, Question(itemCount));

            Assert.NotNull(compiled.SequencePresentation);
            Assert.False(
                IsIdentity(compiled.SequencePresentation!),
                $"presented correct order for {itemCount} items at seed {seed}");
        }
    }

    [Fact]
    public void FixedOrderPresentsTheCorrectOrder()
    {
        // Randomisation off is the author saying "show them as I wrote them",
        // mirroring how a fixed-order matching question behaves.
        var compiled = CompileOne(new QuizSettings { RandomizeAnswerOrder = false }, seed: 7, Question(5));

        Assert.NotNull(compiled.SequencePresentation);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, compiled.SequencePresentation!.ToArray());
    }

    [Fact]
    public void SingleItemPresentsTrivially()
    {
        // Nothing to arrange, so the "never identity" rule does not apply: a
        // lone item is shown as-is rather than forced into a non-existent
        // alternative order.
        var compiled = CompileOne(new QuizSettings { RandomizeAnswerOrder = true }, seed: 1, Question(1));

        Assert.NotNull(compiled.SequencePresentation);
        Assert.Equal(new[] { 0 }, compiled.SequencePresentation!.ToArray());
    }

    [Fact]
    public void CompilingDoesNotDisturbTheModelsItemOrder()
    {
        // The answer key must survive compilation untouched -- the whole reason
        // the shuffle lives on the compiled projection and not on Items.
        var q = Question(5);
        CompileOne(new QuizSettings { RandomizeAnswerOrder = true }, seed: 2, q);

        Assert.Equal(new[] { "Item 0", "Item 1", "Item 2", "Item 3", "Item 4" }, q.Items);
    }

    [Fact]
    public void OnlySequenceQuestionsCarryAPresentation()
    {
        var mc = new MultipleChoiceSingleQuestion { Prompt = "Q" };
        mc.Choices.Add(new Choice { Text = "a", IsCorrect = true });
        mc.Choices.Add(new Choice { Text = "b" });

        var compiled = Compile(new QuizSettings(), seed: 0, mc)
            .Sections.SelectMany(s => s.Questions).Single();

        Assert.Null(compiled.SequencePresentation);
    }

    // --- Web export ----------------------------------------------------------

    [Fact]
    public void WebExportEmbedsTheItemsAndCountForTheBrowserGrader()
    {
        var quiz = Compile(new QuizSettings(), seed: 0, Question(4));
        var html = new QuizWebExporter().Render(quiz, Theme, new WebExportOptions());

        Assert.Contains("\"type\":\"sequence\"", html);
        Assert.Contains("\"count\":4", html);

        // The browser grader's case and the drag list must both be present.
        Assert.Contains("case \"sequence\"", html);
        Assert.Contains("ol.seq", html);

        // Each item carries its authored index so collect() can report the
        // taker's order back in the answer-key domain.
        Assert.Contains("data-index=", html);
    }

    [Fact]
    public void WebExportItemsAreInPresentationOrderNotAnswerOrder()
    {
        // The JSON the browser renders from must match the shuffle, so the drag
        // list does not start in the correct order. We check that the embedded
        // item indices are exactly the compiled presentation.
        var q = Question(6);
        var quiz = Compile(new QuizSettings { RandomizeAnswerOrder = true }, seed: 5, q);
        var presentation = quiz.Sections.SelectMany(s => s.Questions).Single().SequencePresentation!;

        var html = new QuizWebExporter().Render(quiz, Theme, new WebExportOptions());

        // The data-index attributes appear in list order; pull them out and
        // compare to the presentation the compiler produced.
        var indices = System.Text.RegularExpressions.Regex
            .Matches(html, "data-index=\"(\\d+)\"")
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToArray();

        Assert.Equal(presentation.ToArray(), indices);
        Assert.False(IsIdentity(indices));
    }

    [Fact]
    public void WebExportSelfTestBatteryIncludesSequenceCases()
    {
        // The page logs a grader self-test on load; a sequence row there means
        // a broken browser grader shows up in the console rather than silently.
        var html = new QuizWebExporter().Render(Compile(new QuizSettings(), 0, Question(3)), Theme, new WebExportOptions());

        Assert.Contains("type: \"sequence\"", html);
    }

    // --- HTML export ---------------------------------------------------------

    [Fact]
    public void HtmlAnswerKeyShowsTheCorrectOrder()
    {
        var quiz = Compile(new QuizSettings(), seed: 0, Question(3));
        var html = new HtmlExporter().Render(quiz, Theme, new HtmlExportOptions { ShowAnswers = true });

        Assert.Contains("Item 0 -&gt; Item 1 -&gt; Item 2", html);
    }

    [Fact]
    public void HtmlWorksheetListsEveryItem()
    {
        var quiz = Compile(new QuizSettings { RandomizeAnswerOrder = true }, seed: 4, Question(4));
        var html = new HtmlExporter().Render(quiz, Theme, new HtmlExportOptions { ShowAnswers = false });

        // All items are present regardless of the order they are drawn in.
        for (var i = 0; i < 4; i++)
            Assert.Contains($"Item {i}", html);
    }

    // --- Word export ---------------------------------------------------------

    [Fact]
    public void WordExportIncludesItemsAndAnswerKey()
    {
        var quiz = Compile(new QuizSettings(), seed: 0, Question(3));
        var bytes = RenderWord(quiz, showAnswers: true);
        var documentXml = MainDocumentText(bytes);

        for (var i = 0; i < 3; i++)
            Assert.Contains($"Item {i}", documentXml);

        // '>' is XML-escaped in the document part, so the answer key reads with
        // an escaped arrow just as the HTML worksheet does.
        Assert.Contains("Item 0 -&gt; Item 1 -&gt; Item 2", documentXml);
    }

    private static byte[] RenderWord(CompiledQuiz quiz, bool showAnswers)
    {
        using var stream = new MemoryStream();
        new WordExporter().Write(stream, quiz, Theme, new WordExportOptions { ShowAnswers = showAnswers });
        return stream.ToArray();
    }

    private static string MainDocumentText(byte[] docx)
    {
        using var archive = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        var entry = archive.GetEntry("word/document.xml")!;
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    // --- Excel round-trip ----------------------------------------------------

    [Fact]
    public void ExcelRoundTripPreservesItemsAndOrder()
    {
        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "S" };
        section.Questions.Add(Question(4));
        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);

        using var stream = new MemoryStream();
        new ExcelExporter().Write(stream, doc);

        using var read = new MemoryStream(stream.ToArray());
        var result = new ExcelImporter().Read(read);

        Assert.True(result.Success);
        var imported = result.Document!.Sections
            .SelectMany(s => s.Questions)
            .OfType<SequenceQuestion>()
            .Single();

        // Order carries through: the sheet stores items in correct order and the
        // importer reads them back in sheet order, so the answer key survives.
        Assert.Equal(new[] { "Item 0", "Item 1", "Item 2", "Item 3" }, imported.Items);
    }

    [Fact]
    public void ExcelImportRejectsASequenceWithFewerThanTwoItems()
    {
        // A one-item sequence cannot be arranged, so the importer skips it with
        // a note. Paired with a valid question so the file as a whole still
        // imports -- an all-skipped file reports failure, which would mask the
        // specific per-row behaviour we are pinning here.
        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "S" };
        section.Questions.Add(Question(1));
        section.Questions.Add(Question(3));
        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);

        using var stream = new MemoryStream();
        new ExcelExporter().Write(stream, doc);

        using var read = new MemoryStream(stream.ToArray());
        var result = new ExcelImporter().Read(read);

        Assert.True(result.Success);

        var sequences = result.Document!.Sections
            .SelectMany(s => s.Questions)
            .OfType<SequenceQuestion>()
            .ToList();

        // Only the three-item sequence survives; the one-item row is dropped.
        Assert.Single(sequences);
        Assert.Equal(3, sequences[0].Items.Count);
        Assert.Contains(result.Problems, p => p.Contains("sequence", StringComparison.OrdinalIgnoreCase));
    }

    // --- Package format version ----------------------------------------------

    [Fact]
    public void PackageFormatVersionIsAtLeastTwo()
    {
        // The sequence type cannot be deserialised by a version-1 build, so the
        // writer must stamp files at version 2 for the gate to reject them
        // cleanly rather than let them parse into a corrupt document.
        Assert.True(new QuizPackageService().CurrentFormatVersion >= 2);
    }

    // --- Pause / resume ------------------------------------------------------

    [Fact]
    public void ResumingAPausedAttemptKeepsTheSameItemOrder()
    {
        // A resumed sequence must present in the same order the taker was
        // working against. Without persisting the presentation, the rebuilt
        // paper would fall back to the items' correct order -- showing the
        // answer to anyone who paused and came back.
        var q = new SequenceQuestion { Prompt = "Order", Points = 1 };
        q.Items.AddRange(new[] { "Item 0", "Item 1", "Item 2", "Item 3" });

        var attempt = new PausedAttempt
        {
            QuizTitle = "T",
            Sections =
            {
                new PausedSection
                {
                    Title = "S",
                    Questions =
                    {
                        new PausedQuestion
                        {
                            Number = 1,
                            Question = q,
                            SequencePresentation = new List<int> { 2, 0, 3, 1 },
                        },
                    },
                },
            },
        };

        var quiz = PausedAttemptPaper.ToCompiledQuiz(attempt);
        var compiled = quiz.Sections.SelectMany(s => s.Questions).Single();

        Assert.Equal(new[] { 2, 0, 3, 1 }, compiled.SequencePresentation!.ToArray());
    }
}
