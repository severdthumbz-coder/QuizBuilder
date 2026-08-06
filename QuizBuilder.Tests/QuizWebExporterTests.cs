using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using QuizBuilder.Core.Theming;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// The self-grading web export.
///
/// The grading LOGIC is verified separately -- the embedded JavaScript was
/// extracted and run against the same battery as the C# grader, and they agree.
/// These tests cover the C# side: that the page embeds what the browser grader
/// needs, is well-formed, and cannot be broken out of by a hostile prompt.
/// </summary>
public class QuizWebExporterTests
{
    private static readonly QuizWebExporter Exporter = new();
    private static readonly ThemeTokens Theme = BuiltInThemes.Academic();

    private static CompiledQuiz Compile(params Question[] questions)
    {
        var doc = new QuizDocument { Title = "Test Quiz" };
        var section = new Section { Title = "S" };
        foreach (var q in questions) section.Questions.Add(q);
        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);

        return new QuizCompiler().Compile(doc, new QuizSettings(), seed: 0);
    }

    private static string Render(params Question[] questions) =>
        Exporter.Render(Compile(questions), Theme, new WebExportOptions());

    /// <summary>Render a single-question quiz with caller-supplied options.</summary>
    private static string Export(WebExportOptions options) =>
        Exporter.Render(Compile(Single()), Theme, options);

    private static MultipleChoiceSingleQuestion Single()
    {
        var q = new MultipleChoiceSingleQuestion { Prompt = "Pick one", Points = 2 };
        q.Choices.Add(new Choice { Text = "wrong" });
        q.Choices.Add(new Choice { Text = "right", IsCorrect = true });
        return q;
    }

    private static NumericQuestion Numeric()
    {
        return new NumericQuestion
        {
            Prompt = "Acceleration due to gravity?",
            Points = 1,
            Target = 9.8,
            Tolerance = 0.1,
            Unit = "m/s²",
        };
    }

    private static DropdownQuestion Dropdown()
    {
        var q = new DropdownQuestion { Prompt = "Which year?", Points = 1 };
        q.Choices.Add(new Choice { Text = "1990" });
        q.Choices.Add(new Choice { Text = "2000", IsCorrect = true });
        q.Choices.Add(new Choice { Text = "2010" });
        return q;
    }

    [Fact]
    public void NumericRendersDecimalInputAndUnit()
    {
        var html = Render(Numeric());
        Assert.Contains("class=\"numeric\"", html);
        Assert.Contains("inputmode=\"decimal\"", html);
        Assert.Contains("m/s²", html);
        Assert.Contains("data-type=\"numeric\"", html);
    }

    [Fact]
    public void NumericEmitsTargetToleranceUnitInModel()
    {
        var packed = Render(Numeric()).Replace(" ", "");
        Assert.Contains("\"type\":\"numeric\"", packed);
        Assert.Contains("\"target\":9.8", packed);
        Assert.Contains("\"tolerance\":0.1", packed);
    }

    [Fact]
    public void NumericGraderUsesStrictParseNotParseFloat()
    {
        // The embedded grader must use the strict number parse, never the lenient
        // parseFloat, or the browser would score "3.14abc" as correct while the
        // desktop scores it wrong. Proved equivalent in web_numeric_grader_port.py.
        var html = Render(Numeric());
        Assert.Contains("function strictNum", html);
        Assert.Contains("strictNum(ans.text)", html);
        Assert.DoesNotContain("parseFloat(ans.text)", html);
    }

    [Fact]
    public void DropdownRendersSelectWithChoices()
    {
        var html = Render(Dropdown());
        Assert.Contains("class=\"dropdown\"", html);
        Assert.Contains("<select", html);
        Assert.Contains("1990", html);
        Assert.Contains("2000", html);
        Assert.Contains("data-type=\"dropdown\"", html);
    }

    [Fact]
    public void DropdownEmitsChoicesInModel()
    {
        var packed = Render(Dropdown()).Replace(" ", "");
        Assert.Contains("\"type\":\"dropdown\"", packed);
        Assert.Contains("\"correct\":true", packed);
    }

    [Fact]
    public void ThePageIsWellFormedAndSelfContained()
    {
        var html = Render(Single());

        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains("<script>", html);
        Assert.Contains("function scoreQuestion", html);
        Assert.Contains("function grade", html);

        // No external references: a self-contained file works offline.
        Assert.DoesNotContain("src=\"http", html);
        Assert.DoesNotContain("href=\"http", html);
    }

    [Fact]
    public void TheAnswerKeyIsEmbeddedForClientSideGrading()
    {
        // Client-side grading is impossible without the key in the page. This is
        // the honest trade-off the UI warns about.
        var html = Render(Single());

        Assert.Contains("\"correct\":true", html.Replace(" ", ""));
    }

    [Fact]
    public void ThePageWarnsThatItIsSelfAssessment()
    {
        var html = Render(Single());

        Assert.Contains("self-assessment", html);
    }

    [Fact]
    public void EachQuestionTypeRendersItsInput()
    {
        var mc = new MultipleChoiceMultipleQuestion { Prompt = "many", Points = 2 };
        mc.Choices.Add(new Choice { Text = "a", IsCorrect = true });

        var tf = new TrueFalseQuestion { Prompt = "tf", Points = 1, CorrectAnswer = true };

        var sa = new ShortAnswerQuestion { Prompt = "sa", Points = 1 };
        sa.AcceptedAnswers.Add("x");

        var fb = new FillInTheBlankQuestion { Prompt = "fb", Points = 2 };
        fb.Blanks.Add(new Blank { Ordinal = 1, AcceptedAnswers = { "a" } });

        var mq = new MatchingQuestion { Prompt = "mq", Points = 2 };
        mq.Pairs.Add(new MatchPair { Left = "L", Right = "R" });

        var es = new EssayQuestion { Prompt = "es", Points = 5 };

        var html = Render(Single(), mc, tf, sa, fb, mq, es);

        Assert.Contains("type=\"radio\"", html);      // single / true-false
        Assert.Contains("type=\"checkbox\"", html);   // multiple
        Assert.Contains("type=\"text\"", html);       // short / blanks
        Assert.Contains("<select", html);             // matching
        Assert.Contains("<textarea", html);           // essay
    }

    [Fact]
    public void APromptContainingAClosingScriptTagCannotBreakOut()
    {
        // The JSON is embedded in a <script> block. If the encoder did not escape
        // '<', a prompt like "</script>..." would close the block and inject. The
        // default System.Text.Json encoder escapes it to \u003c.
        var q = new MultipleChoiceSingleQuestion
        {
            Prompt = "What is </script><script>alert(1)</script>?",
            Points = 1,
        };
        q.Choices.Add(new Choice { Text = "safe", IsCorrect = true });

        var html = Render(q);

        // The only literal </script> is the real closing tag of the grader block.
        var count = System.Text.RegularExpressions.Regex.Matches(html, "</script>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;

        Assert.Equal(1, count);
    }

    [Fact]
    public void APromptIsHtmlEscapedInTheVisibleQuestion()
    {
        var q = new MultipleChoiceSingleQuestion { Prompt = "5 < 10 & true", Points = 1 };
        q.Choices.Add(new Choice { Text = "ok", IsCorrect = true });

        var html = Render(q);

        Assert.Contains("5 &lt; 10 &amp; true", html);
    }

    [Fact]
    public void TheGraderSelfTestIsPresent()
    {
        // The embedded script runs a battery on load and logs to the console, so
        // a broken grader is visible without reading the source.
        var html = Render(Single());

        Assert.Contains("selfTest", html);
        Assert.Contains("grader self-test", html);
    }

    [Fact]
    public void MatchingOptionsAreEmbeddedSoTheDropdownMatchesTheGrader()
    {
        var mq = new MatchingQuestion { Prompt = "match", Points = 2 };
        mq.Pairs.Add(new MatchPair { Left = "One", Right = "Uno" });
        mq.Pairs.Add(new MatchPair { Left = "Two", Right = "Dos" });

        var html = Render(mq);

        // Both right-hand values appear as <option>s the taker can choose.
        Assert.Contains(">Uno<", html);
        Assert.Contains(">Dos<", html);
    }

    [Fact]
    public void TheDescriptionIsRenderedWithTheSafelist()
    {
        var doc = new QuizDocument { Title = "T", Description = "Read <b>carefully</b>." };
        var section = new Section { Title = "S" };
        section.Questions.Add(Single());
        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);

        var html = Exporter.Render(
            new QuizCompiler().Compile(doc, new QuizSettings(), seed: 0),
            Theme, new WebExportOptions());

        Assert.Contains("<strong>carefully</strong>", html);
    }

    [Fact]
    public void TimerMarkupIsPresentWhenALimitIsSet()
    {
        var html = Export(new WebExportOptions { TimeLimitMinutes = 20 });

        Assert.Contains("id=\"timer\"", html);
        Assert.Contains("\"timeLimitMinutes\":20", html.Replace(" ", ""));
    }

    [Fact]
    public void TimeLimitIsNullInOptionsWhenNotSet()
    {
        var html = Export(new WebExportOptions());   // no limit

        // The bar element is always emitted (hidden), but the option must be null
        // so the countdown never starts.
        Assert.Contains("\"timeLimitMinutes\":null", html.Replace(" ", ""));
    }

    [Fact]
    public void TheCountdownCallsSubmitWhenItReachesZero()
    {
        var html = Export(new WebExportOptions { TimeLimitMinutes = 5 });

        // The auto-submit path must be present: the timer calls submitQuiz on
        // expiry, the same entry point the button uses.
        Assert.Contains("submitQuiz()", html);
        Assert.Contains("startTimer", html);
    }

}
