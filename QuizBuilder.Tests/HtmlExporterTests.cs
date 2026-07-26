using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using QuizBuilder.Core.Theming;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// The escaping and injection cases here were run against a Python port of the
/// same logic first, including a real HTML parser check that hostile prompts
/// render as text rather than markup.
///
/// This matters more than it looks: a quiz prompt is user text, it routinely
/// contains "&lt;" from a maths question, and the output gets emailed to other
/// people who open it in a browser.
/// </summary>
public class HtmlExporterTests
{
    private static CompiledQuiz Compile(QuizDocument doc, QuizSettings? settings = null)
        => new QuizCompiler().Compile(doc, settings ?? new QuizSettings(), seed: 1);

    private static QuizDocument DocWithPrompt(string prompt)
    {
        var doc = new QuizDocument { Title = "Test" };
        var section = new Section { Title = "A" };
        section.Questions.Add(new EssayQuestion { Prompt = prompt, Points = 1 });
        doc.Sections.Add(section);
        return doc;
    }

    private static string Render(QuizDocument doc, bool showAnswers = false, ThemeTokens? theme = null)
        => new HtmlExporter().Render(
            Compile(doc),
            theme ?? BuiltInThemes.ById(BuiltInThemes.AcademicId),
            new HtmlExportOptions { ShowAnswers = showAnswers });

    /// <summary>
    /// Just the markup, with the stylesheet cut out.
    ///
    /// The stylesheet legitimately contains ".correct-mark { ... }" and
    /// ".no-print { display: none }" whatever the options say -- only the
    /// MARKUP is conditional. Asserting "the document does not contain
    /// correct-mark" therefore fails on a perfectly correct page, which is
    /// exactly what happened the first time these were written.
    /// </summary>
    private static string BodyOf(string html)
    {
        var start = html.IndexOf("</style>", StringComparison.Ordinal);
        return start < 0 ? html : html[(start + "</style>".Length)..];
    }

    /// <summary>Just the stylesheet, for asserting on CSS itself.</summary>
    private static string StyleOf(string html)
    {
        var start = html.IndexOf("<style>", StringComparison.Ordinal);
        var end = html.IndexOf("</style>", StringComparison.Ordinal);

        return start < 0 || end < 0 ? string.Empty : html[start..end];
    }

    // --- Escaping -----------------------------------------------------------

    [Fact]
    public void AScriptTagInAPromptIsNotAScriptTag()
    {
        var html = Render(DocWithPrompt("<script>alert(1)</script>"));

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void MathsInAPromptSurvives()
    {
        // The common case, not the adversarial one: someone writes a maths
        // question and the page silently swallows the rest of the line.
        var html = Render(DocWithPrompt("If x < 5 and y > 3, what is z?"));

        Assert.Contains("If x &lt; 5 and y &gt; 3, what is z?", html);
    }

    [Fact]
    public void AttributeBreakoutIsEscaped()
    {
        var html = Render(DocWithPrompt("\" onmouseover=\"alert(1)"));

        Assert.DoesNotContain("onmouseover=\"alert(1)\"", html);
        Assert.Contains("&quot;", html);
    }

    [Fact]
    public void AlreadyEscapedTextIsEscapedAgain()
    {
        // The user typed the characters & a m p ; and must see them back.
        // Trying to detect "already escaped" input is how holes appear.
        var html = Render(DocWithPrompt("a &amp; b"));

        Assert.Contains("a &amp;amp; b", html);
    }

    [Fact]
    public void TheTitleIsEscapedToo()
    {
        var doc = DocWithPrompt("Q");
        doc.Title = "Mid-term <Test> & Review";

        var html = Render(doc);

        Assert.DoesNotContain("<Test>", html);
        Assert.Contains("&lt;Test&gt;", html);
    }

    // --- CSS injection ------------------------------------------------------

    [Fact]
    public void AHostileThemeColourCannotEscapeItsDeclaration()
    {
        var theme = BuiltInThemes.ById(BuiltInThemes.AcademicId).Clone();
        theme.Colors.Primary = "red; } body { display: none } .x {";

        var css = StyleOf(Render(DocWithPrompt("Q"), theme: theme));

        // The attack's own text, not "display: none" -- that phrase appears in
        // the legitimate ".no-print { display: none }" print rule, so asserting
        // on it fails against a perfectly safe page.
        Assert.DoesNotContain("} body {", css);
        Assert.DoesNotContain(".x {", css);

        // and the colour fell back
        Assert.Contains("--primary: #1F3A5F", css);
    }

    [Fact]
    public void AHostileThemeColourCannotCloseTheStyleBlock()
    {
        var theme = BuiltInThemes.ById(BuiltInThemes.AcademicId).Clone();
        theme.Colors.Background = "</style><script>alert(1)</script>";

        var html = Render(DocWithPrompt("Q"), theme: theme);

        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public void AHostileFontFamilyFallsBack()
    {
        var theme = BuiltInThemes.ById(BuiltInThemes.AcademicId).Clone();
        theme.Typography.FontFamily = "Georgia; } body { display: none } .x {";

        var css = StyleOf(Render(DocWithPrompt("Q"), theme: theme));

        // The whole value is rejected, not sanitised: an earlier version
        // deleted the metacharacters and left "font-family: Georgia  body
        // display: none  .x" in the output -- safe, but garbage, and neither
        // the author's font nor a working fallback.
        //
        // Assert on the attack's own distinctive text rather than slicing the
        // CSS apart, which would quietly check the wrong declaration the moment
        // another font-family rule appears above this one.
        Assert.DoesNotContain("Georgia  body", css);
        Assert.DoesNotContain("} body {", css);
        Assert.Contains("font-family: Georgia, serif", css);
    }

    [Fact]
    public void LegitimateFontFamiliesArePreserved()
    {
        var theme = BuiltInThemes.ById(BuiltInThemes.AcademicId).Clone();
        theme.Typography.FontFamily = "\"Times New Roman\", Cambria, serif";

        var css = StyleOf(Render(DocWithPrompt("Q"), theme: theme));

        // Grammar-matched, so quoted names and hyphenated identifiers survive.
        Assert.Contains("Times New Roman", css);
    }

    [Fact]
    public void ValidColoursPassThroughUnchanged()
    {
        var theme = BuiltInThemes.ById(BuiltInThemes.AcademicId).Clone();
        theme.Colors.Primary = "#123456";

        var html = Render(DocWithPrompt("Q"), theme: theme);

        Assert.Contains("--primary: #123456", html);
    }

    [Fact]
    public void EightDigitColoursAreValid()
    {
        // ThemeTokens stores CSS-order #RRGGBBAA, which is valid CSS as-is.
        // That ordering is the whole reason the tokens are plain POCOs.
        var theme = BuiltInThemes.ById(BuiltInThemes.AcademicId).Clone();
        theme.Colors.Border = "#141A1F14";

        var html = Render(DocWithPrompt("Q"), theme: theme);

        Assert.Contains("#141A1F14", html);
    }

    // --- Structure ----------------------------------------------------------

    [Fact]
    public void OutputIsSelfContained()
    {
        var html = Render(DocWithPrompt("Q"));

        // No sibling stylesheet: the file gets emailed, and a page that needs a
        // neighbouring .css arrives broken with no clue why.
        Assert.Contains("<style>", html);
        Assert.DoesNotContain("<link rel=\"stylesheet\"", html);
    }

    [Fact]
    public void PrintRulesKeepAQuestionWhole()
    {
        var html = Render(DocWithPrompt("Q"));

        // Without this a four-option question splits across a page boundary,
        // which is the single thing that makes a printed paper look broken.
        Assert.Contains("@media print", html);
        Assert.Contains("break-inside: avoid", html);
    }

    [Fact]
    public void ThePrintBarIsHiddenWhenPrinting()
    {
        var html = Render(DocWithPrompt("Q"));

        Assert.Contains("no-print", html);
        Assert.Contains("window.print()", html);
    }

    [Fact]
    public void ThePrintBarCanBeOmittedForWebOutput()
    {
        var html = new HtmlExporter().Render(
            Compile(DocWithPrompt("Q")),
            BuiltInThemes.ById(BuiltInThemes.AcademicId),
            new HtmlExportOptions { IncludePrintButton = false });

        Assert.DoesNotContain("window.print()", html);
    }

    // --- Answer key ---------------------------------------------------------

    [Fact]
    public void StudentViewHasNoAnswers()
    {
        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "A" };
        var q = new MultipleChoiceSingleQuestion { Prompt = "Pick", Points = 1 };
        q.Choices.Add(new Choice { Text = "right", IsCorrect = true });
        q.Choices.Add(new Choice { Text = "wrong" });
        section.Questions.Add(q);
        doc.Sections.Add(section);

        var body = BodyOf(Render(doc, showAnswers: false));

        // The BODY, not the document: the stylesheet always defines
        // .correct-mark, only the markup is conditional.
        Assert.DoesNotContain("class=\"answer\"", body);
        Assert.DoesNotContain("correct-mark", body);
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

        var body = BodyOf(Render(doc, showAnswers: true));

        Assert.Contains("correct-mark", body);
        Assert.Contains("&#10003;", body);   // a tick: shape, not colour alone
    }

    [Fact]
    public void TheAnswerKeySaysSo()
    {
        var html = Render(DocWithPrompt("Q"), showAnswers: true);

        Assert.Contains("Answer key", html);
    }

    // --- Content ------------------------------------------------------------

    [Fact]
    public void TheDescriptionIsIncludedWhenPresent()
    {
        var doc = DocWithPrompt("Q");
        doc.Description = "Closed book. 90 minutes.";

        var html = Render(doc);

        Assert.Contains("Closed book. 90 minutes.", html);
    }

    [Fact]
    public void AnEmptyDescriptionEmitsNoParagraph()
    {
        var html = Render(DocWithPrompt("Q"));

        Assert.DoesNotContain("class=\"description\"", html);
    }

    [Fact]
    public void ThePassMarkIsPhrasedForTheChosenBasis()
    {
        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "A" };
        for (var i = 0; i < 4; i++)
            section.Questions.Add(new EssayQuestion { Prompt = $"Q{i}", Points = 1 });
        doc.Sections.Add(section);

        var byQuestions = new HtmlExporter().Render(
            new QuizCompiler().Compile(doc,
                new QuizSettings { PassPercentage = 75, PassMarkBasis = PassMarkBasis.QuestionCount }, 1),
            BuiltInThemes.ById(BuiltInThemes.AcademicId),
            new HtmlExportOptions());

        var byPoints = new HtmlExporter().Render(
            new QuizCompiler().Compile(doc,
                new QuizSettings { PassPercentage = 75, PassMarkBasis = PassMarkBasis.TotalPoints }, 1),
            BuiltInThemes.ById(BuiltInThemes.AcademicId),
            new HtmlExportOptions());

        // A bare "75%" is ambiguous on a weighted paper.
        Assert.Contains("75% of the questions", byQuestions);
        Assert.Contains("75% of the points", byPoints);
    }

    [Fact]
    public void EmptySectionsAreShownRatherThanDropped()
    {
        var doc = new QuizDocument { Title = "T" };
        doc.Sections.Add(new Section { Title = "Empty" });

        var html = Render(doc);

        // A section that vanished would look like a bug.
        Assert.Contains("Empty", html);
        Assert.Contains("no questions in this section", html);
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

        var html = Render(doc);

        Assert.Contains(">3.</span>", html);
        Assert.Contains(">4.</span>", html);
    }

    [Fact]
    public void MatchingIncludesTheShuffledRightColumn()
    {
        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "A" };
        var q = new MatchingQuestion { Prompt = "Match", Points = 3 };
        q.Pairs.Add(new MatchPair { Left = "One", Right = "Uno" });
        q.Pairs.Add(new MatchPair { Left = "Two", Right = "Dos" });
        q.Distractors.Add("Tres");
        section.Questions.Add(q);
        doc.Sections.Add(section);

        var html = Render(doc);

        Assert.Contains("One", html);
        Assert.Contains("Uno", html);
        Assert.Contains("Tres", html);   // the distractor
    }

    [Fact]
    public void AnEmptyQuizStillProducesAValidPage()
    {
        var html = new HtmlExporter().Render(
            Compile(new QuizDocument { Title = "Nothing yet" }),
            BuiltInThemes.ById(BuiltInThemes.AcademicId),
            new HtmlExportOptions());

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("Nothing yet", html);
        Assert.Contains("</html>", html);
    }
}
