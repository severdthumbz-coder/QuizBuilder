using System.IO.Compression;
using System.Xml.Linq;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using QuizBuilder.Core.Theming;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// These unzip the produced .docx and parse the XML, which is the whole reason
/// the format is written directly rather than through DocumentFormat.OpenXml:
/// the output can actually be inspected. A library would have made these tests
/// assert on its own object model rather than on the bytes Word will read.
///
/// The cases here are the ones that silently corrupt a document rather than
/// throwing: a control character in a prompt, a newline that Word treats as
/// whitespace, and the three different measurement units.
/// </summary>
public class WordExporterTests
{
    private static readonly XNamespace W =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static readonly XNamespace XmlNs =
        "http://www.w3.org/XML/1998/namespace";

    private static byte[] Export(QuizDocument doc, bool showAnswers = false,
                                 QuizSettings? settings = null, ThemeTokens? theme = null)
    {
        var quiz = new QuizCompiler().Compile(doc, settings ?? new QuizSettings(), seed: 1);

        using var stream = new MemoryStream();
        new WordExporter().Write(
            stream, quiz,
            theme ?? BuiltInThemes.ById(BuiltInThemes.AcademicId),
            new WordExportOptions { ShowAnswers = showAnswers });

        return stream.ToArray();
    }

    private static XDocument PartOf(byte[] docx, string path)
    {
        using var archive = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        var entry = archive.GetEntry(path);

        Assert.NotNull(entry);

        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }

    private static string TextOf(byte[] docx)
        => string.Concat(PartOf(docx, "word/document.xml").Descendants(W + "t").Select(t => t.Value));

    private static QuizDocument DocWith(string prompt, double points = 1)
    {
        var doc = new QuizDocument { Title = "Test" };
        var section = new Section { Title = "A" };
        section.Questions.Add(new EssayQuestion { Prompt = prompt, Points = points });
        doc.Sections.Add(section);
        return doc;
    }

    private static QuizDocument DocWithQuestion(Question q)
    {
        var doc = new QuizDocument { Title = "Test" };
        var section = new Section { Title = "A" };
        section.Questions.Add(q);
        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);
        return doc;
    }

    [Fact]
    public void NumericAnswerKeyShowsTargetToleranceAndUnit()
    {
        var docx = Export(DocWithQuestion(new NumericQuestion
        {
            Prompt = "How fast?", Target = 9.8, Tolerance = 0.1, Unit = "m/s²",
        }), showAnswers: true);

        var text = TextOf(docx);
        Assert.Contains("9.8", text);
        Assert.Contains("0.1", text);
        Assert.Contains("m/s²", text);
    }

    [Fact]
    public void DropdownAnswerKeyShowsCorrectChoice()
    {
        var dropdown = new DropdownQuestion { Prompt = "Which year?" };
        dropdown.Choices.Add(new Choice { Text = "1990" });
        dropdown.Choices.Add(new Choice { Text = "2000", IsCorrect = true });

        var text = TextOf(Export(DocWithQuestion(dropdown), showAnswers: true));
        Assert.Contains("2000", text);
    }

    // --- Package shape ------------------------------------------------------

    [Fact]
    public void ProducesAValidZipWithEveryRequiredPart()
    {
        var docx = Export(DocWith("Q"));

        using var archive = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        var names = archive.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("[Content_Types].xml", names);
        Assert.Contains("_rels/.rels", names);
        Assert.Contains("word/document.xml", names);
        Assert.Contains("word/styles.xml", names);
        Assert.Contains("word/_rels/document.xml.rels", names);
    }

    [Fact]
    public void EveryPartIsWellFormedXml()
    {
        var docx = Export(DocWith("Q"));

        using var archive = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();

            // Throws on malformed XML, which is the assertion.
            XDocument.Load(stream);
        }
    }

    [Fact]
    public void TheCallersStreamIsLeftOpen()
    {
        var doc = DocWith("Q");
        var quiz = new QuizCompiler().Compile(doc, new QuizSettings(), seed: 1);

        using var stream = new MemoryStream();
        new WordExporter().Write(stream, quiz,
            BuiltInThemes.ById(BuiltInThemes.AcademicId), new WordExportOptions());

        // Closing a caller's stream works until someone wraps this in a using
        // and then reads from it.
        Assert.True(stream.CanRead);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void SectPrIsADirectChildOfBody()
    {
        var document = PartOf(Export(DocWith("Q")), "word/document.xml");
        var body = document.Root!.Element(W + "body");

        // Inside a paragraph it means a section break instead, which is a
        // different thing that would silently repaginate the document.
        Assert.NotNull(body!.Element(W + "sectPr"));
    }

    // --- Text handling ------------------------------------------------------

    [Fact]
    public void MathsInAPromptSurvives()
    {
        var text = TextOf(Export(DocWith("If x < 5 and y > 3, what is z?")));

        Assert.Contains("If x < 5 and y > 3, what is z?", text);
    }

    [Fact]
    public void AmpersandsSurvive()
    {
        var doc = DocWith("Q");
        doc.Title = "Rise & Fall <of Rome>";

        var text = TextOf(Export(doc));

        Assert.Contains("Rise & Fall <of Rome>", text);
    }

    [Fact]
    public void AControlCharacterDoesNotCorruptTheDocument()
    {
        // XML 1.0 forbids most control characters. One of them, pasted into a
        // prompt from somewhere odd, makes Word declare the whole file
        // unreadable rather than skipping that run.
        var text = TextOf(Export(DocWith("before\u0001after")));

        Assert.Contains("beforeafter", text);

        // Ordinal, deliberately. Assert.DoesNotContain(string, string) compares
        // CULTURE-SENSITIVELY, and U+0001 carries no collation weight under ICU
        // -- so as a needle it collates to the empty string and "matches" at
        // position 0 of anything at all. That assertion could never pass,
        // whatever the exporter did. Comparing chars is ordinal by definition.
        Assert.DoesNotContain(text, c => c == '\u0001');
    }

    [Fact]
    public void EveryCharacterInTheDocumentIsLegalXml()
    {
        // The general form of the case above: nothing XML 1.0 forbids may reach
        // the output, whatever the prompt contained.
        var text = TextOf(Export(DocWith("a\u0000b\u0001c\u0008d\u000Be\u001Ff")));

        Assert.DoesNotContain(text, c =>
            c is not ('\t' or '\n' or '\r')
            && (c < ' ' || (c >= '\uD800' && c <= '\uDFFF') || c > '\uFFFD'));

        Assert.Contains("abcdef", text);
    }

    [Fact]
    public void ANewlineBecomesARealLineBreak()
    {
        var document = PartOf(Export(DocWith("first line\nsecond line")), "word/document.xml");

        // A newline inside a w:t is only whitespace to Word: the prompt would
        // collapse onto one line without an explicit break element.
        Assert.NotEmpty(document.Descendants(W + "br"));
    }

    [Fact]
    public void EveryTextRunPreservesWhitespace()
    {
        var document = PartOf(Export(DocWith("Q")), "word/document.xml");

        var runs = document.Descendants(W + "t").ToList();
        Assert.NotEmpty(runs);

        // Without xml:space="preserve" Word strips leading and trailing
        // whitespace, so "A.  option" quietly becomes "A. option" and the
        // indentation of every option row disappears.
        Assert.All(runs, t => Assert.Equal("preserve", t.Attribute(XmlNs + "space")?.Value));
    }

    // --- Units --------------------------------------------------------------

    [Fact]
    public void FontSizesAreInHalfPoints()
    {
        var theme = BuiltInThemes.ById(BuiltInThemes.AcademicId).Clone();
        theme.Typography.BaseSize = 14;

        var styles = PartOf(Export(DocWith("Q"), theme: theme), "word/styles.xml");

        var defaultSize = styles.Descendants(W + "sz").First().Attribute(W + "val")!.Value;

        // w:sz is half-points: 28 means 14pt. Writing 14 there would produce
        // seven-point text, which looks like a rendering bug rather than a
        // unit mistake.
        Assert.Equal("28", defaultSize);
    }

    [Fact]
    public void ThePageIsA4InTwips()
    {
        var document = PartOf(Export(DocWith("Q")), "word/document.xml");
        var pgSz = document.Descendants(W + "pgSz").Single();

        Assert.Equal("11906", pgSz.Attribute(W + "w")!.Value);
        Assert.Equal("16838", pgSz.Attribute(W + "h")!.Value);
    }

    // --- Theme --------------------------------------------------------------

    [Fact]
    public void ColoursHaveNoLeadingHash()
    {
        var styles = PartOf(Export(DocWith("Q")), "word/styles.xml");

        foreach (var color in styles.Descendants(W + "color"))
        {
            var value = color.Attribute(W + "val")!.Value;

            // Word wants a bare hex triplet. A '#' makes it silently fall back
            // to automatic, so the theme would appear not to apply at all.
            Assert.DoesNotContain("#", value);
            Assert.Equal(6, value.Length);
        }
    }

    [Fact]
    public void AnEightDigitCssColourLosesItsAlpha()
    {
        var theme = BuiltInThemes.ById(BuiltInThemes.AcademicId).Clone();
        theme.Colors.TextPrimary = "#12345678";

        var styles = PartOf(Export(DocWith("Q"), theme: theme), "word/styles.xml");
        var values = styles.Descendants(W + "color").Select(c => c.Attribute(W + "val")!.Value).ToList();

        // CSS-order #RRGGBBAA is meaningless to Word; drop the alpha rather
        // than emit eight digits it will reject.
        Assert.Contains("123456", values);
    }

    [Fact]
    public void AHostileThemeColourFallsBack()
    {
        var theme = BuiltInThemes.ById(BuiltInThemes.AcademicId).Clone();
        theme.Colors.TextPrimary = "red; } body { display: none }";

        var styles = PartOf(Export(DocWith("Q"), theme: theme), "word/styles.xml");

        Assert.All(
            styles.Descendants(W + "color").Select(c => c.Attribute(W + "val")!.Value),
            v => Assert.Equal(6, v.Length));
    }

    [Fact]
    public void ACssFontStackBecomesOneFontName()
    {
        var theme = BuiltInThemes.ById(BuiltInThemes.AcademicId).Clone();
        theme.Typography.FontFamily = "\"Times New Roman\", Cambria, serif";

        var styles = PartOf(Export(DocWith("Q"), theme: theme), "word/styles.xml");
        var font = styles.Descendants(W + "rFonts").First().Attribute(W + "ascii")!.Value;

        // A CSS stack means nothing to Word: it takes a single name, quotes
        // included would become part of the name.
        Assert.Equal("Times New Roman", font);
    }

    // --- Content ------------------------------------------------------------

    [Fact]
    public void StudentCopyHasNoAnswers()
    {
        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "A" };
        var q = new MultipleChoiceSingleQuestion { Prompt = "Pick", Points = 1 };
        q.Choices.Add(new Choice { Text = "right", IsCorrect = true });
        q.Choices.Add(new Choice { Text = "wrong" });
        section.Questions.Add(q);
        doc.Sections.Add(section);

        var text = TextOf(Export(doc, showAnswers: false));

        Assert.DoesNotContain("Answer:", text);
        Assert.DoesNotContain("\u2713", text);
    }

    [Fact]
    public void AnswerKeyMarksTheCorrectOption()
    {
        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "A" };
        var q = new MultipleChoiceSingleQuestion { Prompt = "Pick", Points = 1 };
        q.Choices.Add(new Choice { Text = "right", IsCorrect = true });
        q.Choices.Add(new Choice { Text = "wrong" });
        section.Questions.Add(q);
        doc.Sections.Add(section);

        var text = TextOf(Export(doc, showAnswers: true));

        Assert.Contains("Answer: right", text);
        Assert.Contains("\u2713", text);   // a tick: shape, not colour
    }

    [Fact]
    public void QuestionNumbersAreContinuousAcrossSections()
    {
        var doc = new QuizDocument { Title = "T" };
        foreach (var title in new[] { "A", "B" })
        {
            var section = new Section { Title = title };
            section.Questions.Add(new EssayQuestion { Prompt = $"{title}1", Points = 1 });
            section.Questions.Add(new EssayQuestion { Prompt = $"{title}2", Points = 1 });
            doc.Sections.Add(section);
        }

        var text = TextOf(Export(doc));

        Assert.Contains("3.  B1", text);
        Assert.Contains("4.  B2", text);
    }

    [Fact]
    public void ThePassMarkIsPhrasedForTheChosenBasis()
    {
        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "A" };
        for (var i = 0; i < 4; i++)
            section.Questions.Add(new EssayQuestion { Prompt = $"Q{i}", Points = 1 });
        doc.Sections.Add(section);

        var byQuestions = TextOf(Export(doc,
            settings: new QuizSettings { PassPercentage = 75, PassMarkBasis = PassMarkBasis.QuestionCount }));

        var byPoints = TextOf(Export(doc,
            settings: new QuizSettings { PassPercentage = 75, PassMarkBasis = PassMarkBasis.TotalPoints }));

        Assert.Contains("75% of the questions", byQuestions);
        Assert.Contains("75% of the points", byPoints);
    }

    [Fact]
    public void EmptySectionsAreShownRatherThanDropped()
    {
        var doc = new QuizDocument { Title = "T" };
        doc.Sections.Add(new Section { Title = "Empty" });

        var text = TextOf(Export(doc));

        Assert.Contains("Empty", text);
        Assert.Contains("no questions in this section", text);
    }

    [Fact]
    public void AnEmptyQuizStillProducesAValidDocument()
    {
        var docx = Export(new QuizDocument { Title = "Nothing yet" });

        Assert.Contains("Nothing yet", TextOf(docx));
        PartOf(docx, "word/document.xml");   // parses
    }

    [Fact]
    public void EssayQuestionsGetWritingLines()
    {
        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "A" };
        section.Questions.Add(new EssayQuestion { Prompt = "Discuss", Points = 10, SuggestedWordCount = 200 });
        doc.Sections.Add(section);

        var document = PartOf(Export(doc), "word/document.xml");

        var lines = document.Descendants(W + "pStyle")
            .Count(s => s.Attribute(W + "val")?.Value == "AnswerLine");

        // 200 words at roughly ten a line.
        Assert.Equal(20, lines);
    }

    [Fact]
    public void EssayLinesAreCappedSoAHugeWordCountCannotFloodTheDocument()
    {
        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "A" };
        section.Questions.Add(new EssayQuestion { Prompt = "Discuss", Points = 10, SuggestedWordCount = 5000 });
        doc.Sections.Add(section);

        var document = PartOf(Export(doc), "word/document.xml");

        var lines = document.Descendants(W + "pStyle")
            .Count(s => s.Attribute(W + "val")?.Value == "AnswerLine");

        Assert.Equal(40, lines);
    }
}
