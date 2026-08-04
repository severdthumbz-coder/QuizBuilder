using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Grading for the v3 question types — Numeric (target +/- tolerance) and
/// Dropdown (single-choice presented as a dropdown). Numeric's parse/tolerance
/// rules were modelled in tools/port/numeric_grading_port.py first; these pin
/// the results through the real compile+grade pipeline. Dropdown must score
/// identically to single-choice — that equivalence is pinned here too.
/// </summary>
public class NumericDropdownGradingTests
{
    private static readonly QuizGrader Grader = new();

    private static CompiledQuiz Compile(QuizSettings settings, params Question[] questions)
    {
        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "S" };
        foreach (var q in questions) section.Questions.Add(q);
        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);
        return new QuizCompiler().Compile(doc, settings, seed: 1);
    }

    private static AttemptResult Grade(
        CompiledQuiz quiz, QuizSettings settings, params QuestionAnswer[] answers)
    {
        var compiled = quiz.Sections.SelectMany(s => s.Questions).ToList();
        var map = new Dictionary<CompiledQuestion, QuestionAnswer>();
        for (var i = 0; i < compiled.Count && i < answers.Length; i++)
            map[compiled[i]] = answers[i];
        return Grader.Grade(quiz, map, settings, TimeSpan.FromMinutes(1), timedOut: false);
    }

    private static readonly QuizSettings Settings = new();

    // ----- Numeric -------------------------------------------------------- //

    private static NumericQuestion Numeric(double target, double tolerance = 0, double points = 1) =>
        new() { Prompt = "How many?", Target = target, Tolerance = tolerance, Points = points };

    private static QuestionAnswer Typed(string text) => new() { TextAnswer = text };

    [Theory]
    [InlineData("3.14", true)]
    [InlineData("3.15", false)]
    [InlineData("3", false)]
    public void NumericExactMatch(string typed, bool correct)
    {
        var quiz = Compile(Settings, Numeric(3.14));
        var result = Grade(quiz, Settings, Typed(typed));
        Assert.Equal(correct ? 1 : 0, result.ScoredPoints);
    }

    [Theory]
    [InlineData("10.4", true)]
    [InlineData("9.6", true)]
    [InlineData("10.5", true)]   // boundary inclusive
    [InlineData("9.5", true)]    // boundary inclusive
    [InlineData("10.6", false)]
    [InlineData("9.4", false)]
    public void NumericWithinTolerance(string typed, bool correct)
    {
        var quiz = Compile(Settings, Numeric(10.0, tolerance: 0.5));
        var result = Grade(quiz, Settings, Typed(typed));
        Assert.Equal(correct ? 1 : 0, result.ScoredPoints);
    }

    [Theory]
    [InlineData("3")]
    [InlineData("3.0")]
    [InlineData("3.00")]
    [InlineData("+3")]
    [InlineData("  3  ")]
    public void NumericIntegerDecimalAndWhitespace(string typed)
    {
        var quiz = Compile(Settings, Numeric(3.0));
        var result = Grade(quiz, Settings, Typed(typed));
        Assert.Equal(1, result.ScoredPoints);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("3x")]
    [InlineData("3.1.4")]
    [InlineData("inf")]
    [InlineData("nan")]
    public void NumericBlankOrGarbageScoresZero(string typed)
    {
        var quiz = Compile(Settings, Numeric(5.0, tolerance: 1.0));
        var result = Grade(quiz, Settings, Typed(typed));
        Assert.Equal(0, result.ScoredPoints);
    }

    [Fact]
    public void NumericNegativeToleranceClampedToExact()
    {
        var quiz = Compile(Settings, Numeric(10.0, tolerance: -5.0));
        Assert.Equal(1, Grade(quiz, Settings, Typed("10")).ScoredPoints);
        Assert.Equal(0, Grade(quiz, Settings, Typed("12")).ScoredPoints);
    }

    [Fact]
    public void NumericNegativeTargetAndPoints()
    {
        var quiz = Compile(Settings, Numeric(-5.0, tolerance: 0.1, points: 2.5));
        Assert.Equal(2.5, Grade(quiz, Settings, Typed("-5.05")).ScoredPoints);
        Assert.Equal(0, Grade(quiz, Settings, Typed("5")).ScoredPoints);
    }

    [Fact]
    public void NumericScientificNotation()
    {
        var quiz = Compile(Settings, Numeric(1000.0));
        Assert.Equal(1, Grade(quiz, Settings, Typed("1e3")).ScoredPoints);
    }

    // ----- Dropdown ------------------------------------------------------- //

    private static DropdownQuestion Dropdown(double points = 1)
    {
        var q = new DropdownQuestion { Prompt = "Which year?", Points = points };
        q.Choices.Add(new Choice { Text = "1990" });
        q.Choices.Add(new Choice { Text = "2000", IsCorrect = true });
        q.Choices.Add(new Choice { Text = "2010" });
        return q;
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(2, false)]
    public void DropdownScoresChosenIndex(int index, bool correct)
    {
        var quiz = Compile(Settings, Dropdown(points: 3));
        var result = Grade(quiz, Settings, new QuestionAnswer { ChoiceIndex = index });
        Assert.Equal(correct ? 3 : 0, result.ScoredPoints);
    }

    [Fact]
    public void DropdownNoSelectionScoresZero()
    {
        var quiz = Compile(Settings, Dropdown());
        var result = Grade(quiz, Settings, new QuestionAnswer());
        Assert.Equal(0, result.ScoredPoints);
    }

    [Fact]
    public void DropdownOutOfRangeIndexScoresZero()
    {
        var quiz = Compile(Settings, Dropdown());
        var result = Grade(quiz, Settings, new QuestionAnswer { ChoiceIndex = 99 });
        Assert.Equal(0, result.ScoredPoints);
    }

    [Fact]
    public void DropdownMatchesSingleChoiceScoring()
    {
        // Same options + same pick must score the same whether modelled as a
        // dropdown or a single-choice question — the equivalence that justifies
        // reusing the scoring logic.
        var dropdown = Dropdown(points: 4);
        var single = new MultipleChoiceSingleQuestion { Prompt = "Which year?", Points = 4 };
        single.Choices.Add(new Choice { Text = "1990" });
        single.Choices.Add(new Choice { Text = "2000", IsCorrect = true });
        single.Choices.Add(new Choice { Text = "2010" });

        foreach (var index in new[] { 0, 1, 2 })
        {
            var dq = Compile(Settings, dropdown);
            var sq = Compile(Settings, single);
            var dResult = Grade(dq, Settings, new QuestionAnswer { ChoiceIndex = index });
            var sResult = Grade(sq, Settings, new QuestionAnswer { ChoiceIndex = index });
            Assert.Equal(sResult.ScoredPoints, dResult.ScoredPoints);
        }
    }
}
