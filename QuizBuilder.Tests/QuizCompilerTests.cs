using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Ported from a Python reference model run against these cases before any C#
/// existed. The cases that matter are the ones where a naive implementation
/// throws or silently produces a wrong paper: asking for more questions than a
/// section holds, a count of zero, a section with no configured count, and
/// whether the same seed reproduces the same paper.
/// </summary>
public class QuizCompilerTests
{
    private static QuizDocument DocWith(params (string title, int questionCount)[] sections)
    {
        var doc = new QuizDocument { Title = "Test Quiz" };

        foreach (var (title, count) in sections)
        {
            var section = new Section { Title = title };
            for (var i = 0; i < count; i++)
                section.Questions.Add(new EssayQuestion { Prompt = $"{title} Q{i + 1}", Points = 1 });

            doc.Sections.Add(section);
            doc.SectionDisplayOrder.Add(section.Id);
        }

        return doc;
    }

    private static QuizSettings AllQuestions() => new()
    {
        SelectionMode = QuestionSelectionMode.AllQuestions,
    };

    [Fact]
    public void AllQuestions_TakesEverythingInOrder()
    {
        var doc = DocWith(("A", 3), ("B", 2));

        var result = new QuizCompiler().Compile(doc, AllQuestions(), seed: 1);

        Assert.Equal(5, result.QuestionCount);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 },
            result.Sections.SelectMany(s => s.Questions).Select(q => q.Number));
    }

    [Fact]
    public void ExactCount_TakesTheRequestedNumber()
    {
        var doc = DocWith(("A", 4), ("B", 2));
        var settings = new QuizSettings
        {
            SelectionMode = QuestionSelectionMode.ExactCountPerSection,
            QuestionCountPerSection =
            {
                [doc.Sections[0].Id.ToString()] = 2,
                [doc.Sections[1].Id.ToString()] = 1,
            },
        };

        var result = new QuizCompiler().Compile(doc, settings, seed: 1);

        Assert.Equal(2, result.Sections[0].Questions.Count);
        Assert.Single(result.Sections[1].Questions);
    }

    [Fact]
    public void ExactCount_AskingForMoreThanExists_UsesAllAndWarns()
    {
        var doc = DocWith(("A", 4));
        var settings = new QuizSettings
        {
            SelectionMode = QuestionSelectionMode.ExactCountPerSection,
            QuestionCountPerSection = { [doc.Sections[0].Id.ToString()] = 99 },
        };

        var result = new QuizCompiler().Compile(doc, settings, seed: 1);

        // A naive Take-random-N implementation throws here.
        Assert.Equal(4, result.Sections[0].Questions.Count);
        Assert.Contains(result.Warnings, w => w.Contains("only has 4"));
    }

    [Fact]
    public void ExactCount_OfZero_LeavesTheSectionEmptyButPresent()
    {
        var doc = DocWith(("A", 3), ("B", 2));
        var settings = new QuizSettings
        {
            SelectionMode = QuestionSelectionMode.ExactCountPerSection,
            QuestionCountPerSection = { [doc.Sections[0].Id.ToString()] = 0 },
        };

        var result = new QuizCompiler().Compile(doc, settings, seed: 1);

        // Present but empty: a section that vanished would look like a bug,
        // whereas an empty one shows the setting doing what it was told.
        Assert.Equal(2, result.Sections.Count);
        Assert.Empty(result.Sections[0].Questions);
        Assert.Contains(result.Warnings, w => w.Contains("0 questions"));
    }

    [Fact]
    public void ExactCount_SectionWithNoConfiguredCount_TakesEverything()
    {
        var doc = DocWith(("A", 3), ("B", 2));
        var settings = new QuizSettings
        {
            SelectionMode = QuestionSelectionMode.ExactCountPerSection,
            QuestionCountPerSection = { [doc.Sections[0].Id.ToString()] = 1 },
            // B has no entry at all.
        };

        var result = new QuizCompiler().Compile(doc, settings, seed: 1);

        // Defaulting to zero would silently delete B from the paper.
        Assert.Equal(2, result.Sections[1].Questions.Count);
    }

    [Fact]
    public void SameSeed_ProducesTheSamePaper()
    {
        var doc = DocWith(("A", 8));
        var settings = new QuizSettings
        {
            SelectionMode = QuestionSelectionMode.ExactCountPerSection,
            QuestionCountPerSection = { [doc.Sections[0].Id.ToString()] = 4 },
            RandomizeQuestionOrder = true,
        };

        var first = new QuizCompiler().Compile(doc, settings, seed: 42);
        var second = new QuizCompiler().Compile(doc, settings, seed: 42);

        // Without this, every repaint of the Preview tab reshuffles the paper.
        Assert.Equal(
            first.Sections[0].Questions.Select(q => q.Question.Prompt),
            second.Sections[0].Questions.Select(q => q.Question.Prompt));
    }

    [Fact]
    public void DifferentSeed_ProducesADifferentPaper()
    {
        var doc = DocWith(("A", 10));
        var settings = new QuizSettings { RandomizeQuestionOrder = true };

        var a = new QuizCompiler().Compile(doc, settings, seed: 1);
        var b = new QuizCompiler().Compile(doc, settings, seed: 2);

        Assert.NotEqual(
            a.Sections[0].Questions.Select(q => q.Question.Prompt),
            b.Sections[0].Questions.Select(q => q.Question.Prompt));
    }

    [Fact]
    public void Compiling_DoesNotMutateTheAuthoredDocument()
    {
        var doc = new QuizDocument();
        var section = new Section { Title = "A" };
        var question = new MultipleChoiceSingleQuestion { Prompt = "Q" };
        question.Choices.Add(new Choice { Text = "first" });
        question.Choices.Add(new Choice { Text = "second" });
        question.Choices.Add(new Choice { Text = "third" });
        section.Questions.Add(question);
        doc.Sections.Add(section);

        var settings = new QuizSettings { RandomizeAnswerOrder = true };

        for (var seed = 0; seed < 20; seed++)
            new QuizCompiler().Compile(doc, settings, seed);

        // The author's own document must survive being previewed. Shuffling in
        // place would silently reorder their choices under them.
        Assert.Equal(new[] { "first", "second", "third" },
            question.Choices.Select(c => c.Text));
    }

    [Fact]
    public void TrueFalse_IsNeverShuffled()
    {
        var doc = new QuizDocument();
        var section = new Section { Title = "A" };
        section.Questions.Add(new TrueFalseQuestion { Prompt = "Q", CorrectAnswer = true });
        doc.Sections.Add(section);

        var settings = new QuizSettings { RandomizeAnswerOrder = true };
        var result = new QuizCompiler().Compile(doc, settings, seed: 7);

        // Nothing to assert about order -- the point is that it compiles and
        // the answer survives. A paper reading "False / True" looks broken.
        var compiled = Assert.IsType<TrueFalseQuestion>(result.Sections[0].Questions[0].Question);
        Assert.True(compiled.CorrectAnswer);
    }

    [Fact]
    public void Matching_OptionsIncludeDistractors()
    {
        var doc = new QuizDocument();
        var section = new Section { Title = "A" };
        var question = new MatchingQuestion { Prompt = "Match these" };
        question.Pairs.Add(new MatchPair { Left = "1", Right = "one" });
        question.Pairs.Add(new MatchPair { Left = "2", Right = "two" });
        question.Distractors.Add("three");
        section.Questions.Add(question);
        doc.Sections.Add(section);

        var result = new QuizCompiler().Compile(doc, new QuizSettings(), seed: 1);

        var options = result.Sections[0].Questions[0].MatchingOptions;
        Assert.NotNull(options);
        Assert.Equal(3, options!.Count);
        Assert.Contains("three", options);
    }

    [Fact]
    public void Matching_OptionsAreOnlyBuiltForMatchingQuestions()
    {
        var doc = DocWith(("A", 1));

        var result = new QuizCompiler().Compile(doc, new QuizSettings(), seed: 1);

        Assert.Null(result.Sections[0].Questions[0].MatchingOptions);
    }

    [Fact]
    public void SectionDisplayOrder_IsRespected()
    {
        var doc = DocWith(("First", 1), ("Second", 1));

        // Publish order deliberately differs from authoring order.
        doc.SectionDisplayOrder = new List<Guid> { doc.Sections[1].Id, doc.Sections[0].Id };

        var result = new QuizCompiler().Compile(doc, AllQuestions(), seed: 1);

        Assert.Equal("Second", result.Sections[0].Title);
        Assert.Equal("First", result.Sections[1].Title);
    }

    [Fact]
    public void QuestionNumbering_IsContinuousAcrossSections()
    {
        var doc = DocWith(("A", 2), ("B", 2));

        var result = new QuizCompiler().Compile(doc, AllQuestions(), seed: 1);

        Assert.Equal(new[] { 1, 2 }, result.Sections[0].Questions.Select(q => q.Number));
        Assert.Equal(new[] { 3, 4 }, result.Sections[1].Questions.Select(q => q.Number));
    }

    [Fact]
    public void EmptyQuiz_WarnsRatherThanThrowing()
    {
        var result = new QuizCompiler().Compile(new QuizDocument(), AllQuestions(), seed: 1);

        Assert.Empty(result.Sections);
        Assert.Contains(result.Warnings, w => w.Contains("no sections"));
    }

    [Fact]
    public void TotalPoints_SumsTheCompiledPaper_NotTheWholeDocument()
    {
        var doc = DocWith(("A", 4));
        var settings = new QuizSettings
        {
            SelectionMode = QuestionSelectionMode.ExactCountPerSection,
            QuestionCountPerSection = { [doc.Sections[0].Id.ToString()] = 2 },
        };

        var result = new QuizCompiler().Compile(doc, settings, seed: 1);

        // 2 of 4 questions at 1 point each. Reporting the document's total
        // would tell the student the paper is worth twice what it is.
        Assert.Equal(2, result.TotalPoints);
    }

    // --- Pass mark ---------------------------------------------------------

    [Fact]
    public void PointsToPass_RoundsUp_SoAHairUnderIsAFail()
    {
        var doc = DocWith(("A", 37));   // 37 questions at 1 point = 37 points
        var settings = new QuizSettings { PassPercentage = 60, PassMarkBasis = PassMarkBasis.TotalPoints };

        var result = new QuizCompiler().Compile(doc, settings, seed: 1);

        // 60% of 37 = 22.2 exactly. Rounding to nearest would set the bar at 22
        // and let a 22-point paper pass a 22.2 requirement.
        Assert.Equal(22.2, result.PointsToPass, precision: 2);
    }

    [Fact]
    public void Passes_AtExactlyTheMark_IsAPass()
    {
        var doc = DocWith(("A", 100));
        var settings = new QuizSettings { PassPercentage = 50, PassMarkBasis = PassMarkBasis.TotalPoints };

        var result = new QuizCompiler().Compile(doc, settings, seed: 1);

        Assert.True(result.PassesOnPoints(50));
    }

    [Fact]
    public void Passes_AHairUnder_IsAFail()
    {
        var doc = DocWith(("A", 100));
        var settings = new QuizSettings { PassPercentage = 50, PassMarkBasis = PassMarkBasis.TotalPoints };

        var result = new QuizCompiler().Compile(doc, settings, seed: 1);

        Assert.False(result.PassesOnPoints(49.99));
    }

    [Fact]
    public void Passes_OnAPaperWorthNothing_IsNeitherPassNorFail()
    {
        var settings = new QuizSettings { PassPercentage = 50, PassMarkBasis = PassMarkBasis.TotalPoints };

        var result = new QuizCompiler().Compile(new QuizDocument(), settings, seed: 1);

        // A straight division would throw or return NaN here.
        Assert.Null(result.PassesOnPoints(0));
        Assert.Equal(0, result.PointsToPass);
    }

    [Fact]
    public void PassMarkOfZero_WarnsThatEveryonePasses()
    {
        var doc = DocWith(("A", 2));
        var settings = new QuizSettings { PassPercentage = 0, PassMarkBasis = PassMarkBasis.TotalPoints };

        var result = new QuizCompiler().Compile(doc, settings, seed: 1);

        Assert.True(result.PassesOnPoints(0));
        Assert.Contains(result.Warnings, w => w.Contains("0%"));
    }

    [Fact]
    public void PassMarkOfOneHundred_RequiresEverything()
    {
        var doc = DocWith(("A", 10));
        var settings = new QuizSettings { PassPercentage = 100, PassMarkBasis = PassMarkBasis.TotalPoints };

        var result = new QuizCompiler().Compile(doc, settings, seed: 1);

        Assert.False(result.PassesOnPoints(9));
        Assert.True(result.PassesOnPoints(10));
        // Legal and sometimes intended, so no warning.
        Assert.DoesNotContain(result.Warnings, w => w.Contains("pass mark"));
    }

    [Fact]
    public void PassMark_IsAgainstTheCompiledPaper_NotTheWholeDocument()
    {
        var doc = DocWith(("A", 10));
        var settings = new QuizSettings
        {
            SelectionMode = QuestionSelectionMode.ExactCountPerSection,
            QuestionCountPerSection = { [doc.Sections[0].Id.ToString()] = 4 },
            PassPercentage = 50,
            PassMarkBasis = PassMarkBasis.TotalPoints,
        };

        var result = new QuizCompiler().Compile(doc, settings, seed: 1);

        // 4 questions on the paper, not 10: the bar is 2 points, not 5.
        Assert.Equal(4, result.TotalPoints);
        Assert.Equal(2, result.PointsToPass);
    }

    [Fact]
    public void PointsToPass_CeilingBites_OnFractionalPointPapers()
    {
        // 3 questions at 2.5 points = 7.5 total. 33% of that is 2.475.
        var doc = new QuizDocument();
        var section = new Section { Title = "A" };
        for (var i = 0; i < 3; i++)
            section.Questions.Add(new EssayQuestion { Prompt = $"Q{i}", Points = 2.5 });
        doc.Sections.Add(section);

        var settings = new QuizSettings { PassPercentage = 33, PassMarkBasis = PassMarkBasis.TotalPoints };
        var result = new QuizCompiler().Compile(doc, settings, seed: 1);

        Assert.Equal(7.5, result.TotalPoints, precision: 2);

        // Displayed bar rounds UP to 2.48, never down to 2.47: telling someone
        // they need 2.47 and then failing them on 2.475 is indefensible.
        Assert.Equal(2.48, result.PointsToPass, precision: 2);
    }

    [Fact]
    public void Passes_UsesTheExactPercentage_NotTheDisplayedBar()
    {
        var doc = new QuizDocument();
        var section = new Section { Title = "A" };
        for (var i = 0; i < 3; i++)
            section.Questions.Add(new EssayQuestion { Prompt = $"Q{i}", Points = 2.5 });
        doc.Sections.Add(section);

        var result = new QuizCompiler().Compile(doc, new QuizSettings { PassPercentage = 33, PassMarkBasis = PassMarkBasis.TotalPoints }, seed: 1);

        // Exactly on the real bar (2.475) passes, even though the displayed
        // bar says 2.48. The display gives ground, the rule does not move.
        Assert.True(result.PassesOnPoints(2.475));
        Assert.False(result.PassesOnPoints(2.474));
    }

    // --- Question-based passing --------------------------------------------

    /// <summary>3 one-point MC questions plus one ten-point essay.</summary>
    private static QuizDocument WeightedPaper()
    {
        var doc = new QuizDocument();
        var section = new Section { Title = "Mixed" };

        for (var i = 0; i < 3; i++)
            section.Questions.Add(new MultipleChoiceSingleQuestion { Prompt = $"MC{i}", Points = 1 });

        section.Questions.Add(new EssayQuestion { Prompt = "Essay", Points = 10 });
        doc.Sections.Add(section);

        return doc;
    }

    [Fact]
    public void TheTwoModesDisagreeOnAWeightedPaper()
    {
        // This is the whole reason both modes exist. A student aces the three
        // MC questions and leaves the essay blank: 3 of 4 questions right, but
        // only 3 of 13 marks.
        var doc = WeightedPaper();

        var byQuestions = new QuizCompiler().Compile(
            doc, new QuizSettings { PassPercentage = 75, PassMarkBasis = PassMarkBasis.QuestionCount }, 1);
        var byPoints = new QuizCompiler().Compile(
            doc, new QuizSettings { PassPercentage = 75, PassMarkBasis = PassMarkBasis.TotalPoints }, 1);

        var scores = ScoreAllButLast(byQuestions);

        Assert.True(byQuestions.Passes(scores));
        Assert.False(byPoints.Passes(ScoreAllButLast(byPoints)));
    }

    /// <summary>Full marks on every question except the last, which scores zero.</summary>
    private static Dictionary<CompiledQuestion, double> ScoreAllButLast(CompiledQuiz quiz)
    {
        var all = quiz.Sections.SelectMany(s => s.Questions).ToList();

        return all.ToDictionary(
            q => q,
            q => ReferenceEquals(q, all[^1]) ? 0d : q.Question.Points);
    }

    [Fact]
    public void QuestionCount_HalfMarksCountsAsCorrect()
    {
        var doc = WeightedPaper();
        var quiz = new QuizCompiler().Compile(
            doc, new QuizSettings { PassPercentage = 100, PassMarkBasis = PassMarkBasis.QuestionCount }, 1);

        var questions = quiz.Sections.SelectMany(s => s.Questions).ToList();
        var essay = questions.Single(q => q.Question is EssayQuestion);

        // Every MC correct, essay on exactly half marks. Half counts, so 100%
        // of questions are "correct" and even a 100% bar passes.
        var scores = questions.ToDictionary(
            q => q,
            q => ReferenceEquals(q, essay) ? 5d : q.Question.Points);

        Assert.True(quiz.Passes(scores));
    }

    [Fact]
    public void QuestionCount_JustUnderHalfDoesNotCount()
    {
        var doc = WeightedPaper();
        var quiz = new QuizCompiler().Compile(
            doc, new QuizSettings { PassPercentage = 100, PassMarkBasis = PassMarkBasis.QuestionCount }, 1);

        var questions = quiz.Sections.SelectMany(s => s.Questions).ToList();
        var essay = questions.Single(q => q.Question is EssayQuestion);

        var scores = questions.ToDictionary(
            q => q,
            q => ReferenceEquals(q, essay) ? 4.99d : q.Question.Points);

        Assert.False(quiz.Passes(scores));
    }

    [Fact]
    public void QuestionIsCorrect_AtTheHalfwayBoundary()
    {
        Assert.True(CompiledQuiz.QuestionIsCorrect(5, 10));
        Assert.False(CompiledQuiz.QuestionIsCorrect(4.99, 10));
        Assert.True(CompiledQuiz.QuestionIsCorrect(10, 10));
        Assert.False(CompiledQuiz.QuestionIsCorrect(0, 10));
    }

    [Fact]
    public void QuestionIsCorrect_OnAZeroPointQuestion_IsUndefined()
        => Assert.Null(CompiledQuiz.QuestionIsCorrect(0, 0));

    [Fact]
    public void QuestionsToPass_RoundsUp()
    {
        var doc = DocWith(("A", 5));
        var quiz = new QuizCompiler().Compile(
            doc, new QuizSettings { PassPercentage = 75, PassMarkBasis = PassMarkBasis.QuestionCount }, 1);

        // 75% of 5 is 3.75. Three of five is 60%, not 75%, so the bar is 4.
        Assert.Equal(4, quiz.QuestionsToPass);
    }

    [Fact]
    public void QuestionsToPass_ExactDivision()
    {
        var doc = DocWith(("A", 4));
        var quiz = new QuizCompiler().Compile(
            doc, new QuizSettings { PassPercentage = 75, PassMarkBasis = PassMarkBasis.QuestionCount }, 1);

        Assert.Equal(3, quiz.QuestionsToPass);
    }

    [Fact]
    public void ZeroPointQuestions_AreExcludedFromTheCount()
    {
        var doc = new QuizDocument();
        var section = new Section { Title = "A" };
        section.Questions.Add(new EssayQuestion { Prompt = "Real", Points = 5 });
        section.Questions.Add(new EssayQuestion { Prompt = "Ungraded", Points = 0 });
        doc.Sections.Add(section);

        var quiz = new QuizCompiler().Compile(
            doc, new QuizSettings { PassPercentage = 100, PassMarkBasis = PassMarkBasis.QuestionCount }, 1);

        Assert.Equal(1, quiz.GradeableQuestionCount);

        // Counting the 0-point question as incorrect would put 100% out of
        // reach through no fault of the student.
        var questions = quiz.Sections.SelectMany(s => s.Questions).ToList();
        var real = questions.Single(q => q.Question.Points > 0);
        var scores = new Dictionary<CompiledQuestion, double> { [real] = 5 };

        Assert.True(quiz.Passes(scores));
    }

    [Fact]
    public void QuestionCount_AnUnansweredQuestionCountsAsZero()
    {
        var doc = DocWith(("A", 4));
        var quiz = new QuizCompiler().Compile(
            doc, new QuizSettings { PassPercentage = 75, PassMarkBasis = PassMarkBasis.QuestionCount }, 1);

        var questions = quiz.Sections.SelectMany(s => s.Questions).ToList();

        // Only three of the four are in the dictionary at all.
        var scores = questions.Take(3).ToDictionary(q => q, q => q.Question.Points);

        // 3 of 4 = 75%, exactly the bar.
        Assert.True(quiz.Passes(scores));

        var fewer = questions.Take(2).ToDictionary(q => q, q => q.Question.Points);
        Assert.False(quiz.Passes(fewer));
    }

    [Fact]
    public void QuestionCount_OnAnEmptyPaper_IsUndefined()
    {
        var quiz = new QuizCompiler().Compile(
            new QuizDocument(),
            new QuizSettings { PassPercentage = 75, PassMarkBasis = PassMarkBasis.QuestionCount }, 1);

        Assert.Null(quiz.Passes(new Dictionary<CompiledQuestion, double>()));
        Assert.Equal(0, quiz.QuestionsToPass);
    }

    [Fact]
    public void DefaultBasis_IsQuestionCount()
        => Assert.Equal(PassMarkBasis.QuestionCount, new QuizSettings().PassMarkBasis);
}
