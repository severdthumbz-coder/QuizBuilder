using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// The scoring rules.
///
/// These matter more than most: a grader that is subtly generous produces
/// plausible numbers forever and nothing ever reports it. Every rule was
/// modelled and run before it was written, and these pin the results.
/// </summary>
public class QuizGraderTests
{
    private static readonly QuizGrader Grader = new();

    /// <summary>Compiles a one-section paper so the tests exercise the real pipeline.</summary>
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

    private static MultipleChoiceSingleQuestion SingleQuestion(double points = 2)
    {
        var q = new MultipleChoiceSingleQuestion { Prompt = "Pick", Points = points };
        q.Choices.Add(new Choice { Text = "wrong" });
        q.Choices.Add(new Choice { Text = "right", IsCorrect = true });
        return q;
    }

    private static MultipleChoiceMultipleQuestion MultiQuestion(bool partial, double points = 4)
    {
        var q = new MultipleChoiceMultipleQuestion
        {
            Prompt = "Pick many",
            Points = points,
            AllowPartialCredit = partial,
        };

        q.Choices.Add(new Choice { Text = "a", IsCorrect = true });
        q.Choices.Add(new Choice { Text = "b", IsCorrect = true });
        q.Choices.Add(new Choice { Text = "c" });
        q.Choices.Add(new Choice { Text = "d" });

        return q;
    }

    // --- Multiple choice, single -------------------------------------------

    [Fact]
    public void CorrectSingleChoiceScoresFull()
    {
        var settings = new QuizSettings();
        var quiz = Compile(settings, SingleQuestion());

        var result = Grade(quiz, settings, new QuestionAnswer { ChoiceIndex = 1 });

        Assert.Equal(2, result.ScoredPoints);
        Assert.Equal(100, result.Percentage);
    }

    [Fact]
    public void WrongSingleChoiceScoresNothing()
    {
        var settings = new QuizSettings();
        var quiz = Compile(settings, SingleQuestion());

        var result = Grade(quiz, settings, new QuestionAnswer { ChoiceIndex = 0 });

        Assert.Equal(0, result.ScoredPoints);
    }

    [Fact]
    public void AnUnansweredQuestionScoresZeroRatherThanBeingSkipped()
    {
        // Skipping it would shrink the denominator and inflate the result.
        var settings = new QuizSettings();
        var quiz = Compile(settings, SingleQuestion());

        var result = Grade(quiz, settings, new QuestionAnswer());

        Assert.Equal(0, result.ScoredPoints);
        Assert.Equal(2, result.AutoGradedPoints);
        Assert.Equal(0, result.Percentage);
        Assert.False(result.Results[0].WasAnswered);
    }

    [Fact]
    public void AnOutOfRangeChoiceIndexScoresZeroRatherThanThrowing()
    {
        var settings = new QuizSettings();
        var quiz = Compile(settings, SingleQuestion());

        var result = Grade(quiz, settings, new QuestionAnswer { ChoiceIndex = 99 });

        Assert.Equal(0, result.ScoredPoints);
    }

    // --- Multiple choice, multiple -----------------------------------------

    [Fact]
    public void ExactSetScoresFullWithoutPartialCredit()
    {
        var settings = new QuizSettings();
        var quiz = Compile(settings, MultiQuestion(partial: false));

        var result = Grade(quiz, settings, new QuestionAnswer { ChoiceIndices = { 0, 1 } });

        Assert.Equal(4, result.ScoredPoints);
    }

    [Fact]
    public void AlmostRightScoresNothingWithoutPartialCredit()
    {
        var settings = new QuizSettings();
        var quiz = Compile(settings, MultiQuestion(partial: false));

        var result = Grade(quiz, settings, new QuestionAnswer { ChoiceIndices = { 0 } });

        Assert.Equal(0, result.ScoredPoints);
    }

    [Fact]
    public void HalfRightScoresHalfWithPartialCredit()
    {
        var settings = new QuizSettings();
        var quiz = Compile(settings, MultiQuestion(partial: true));

        var result = Grade(quiz, settings, new QuestionAnswer { ChoiceIndices = { 0 } });

        Assert.Equal(2, result.ScoredPoints);
    }

    [Fact]
    public void SelectingEveryBoxScoresNothing()
    {
        // THE rule that makes partial credit worth having. The obvious
        // implementation -- hits / correctCount -- gives full marks for ticking
        // everything, which makes the question free. This one nets
        // (2 hits - 2 misses) / 2 = 0.
        var settings = new QuizSettings();
        var quiz = Compile(settings, MultiQuestion(partial: true));

        var result = Grade(quiz, settings,
            new QuestionAnswer { ChoiceIndices = { 0, 1, 2, 3 } });

        Assert.Equal(0, result.ScoredPoints);
    }

    [Fact]
    public void AWrongPickCancelsARightOne()
    {
        var settings = new QuizSettings();
        var quiz = Compile(settings, MultiQuestion(partial: true));

        // Both right + one wrong = (2 - 1) / 2 = half.
        var result = Grade(quiz, settings,
            new QuestionAnswer { ChoiceIndices = { 0, 1, 2 } });

        Assert.Equal(2, result.ScoredPoints);
    }

    [Fact]
    public void AllWrongFloorsAtZeroAndNeverGoesNegative()
    {
        // A negative would eat marks earned on other questions.
        var settings = new QuizSettings();
        var quiz = Compile(settings, MultiQuestion(partial: true));

        var result = Grade(quiz, settings, new QuestionAnswer { ChoiceIndices = { 2, 3 } });

        Assert.Equal(0, result.ScoredPoints);
    }

    // --- True/false ---------------------------------------------------------

    [Fact]
    public void TrueFalseScoresAllOrNothing()
    {
        var settings = new QuizSettings();
        var quiz = Compile(settings, new TrueFalseQuestion { Prompt = "?", Points = 1, CorrectAnswer = true });

        Assert.Equal(1, Grade(quiz, settings, new QuestionAnswer { BoolAnswer = true }).ScoredPoints);
        Assert.Equal(0, Grade(quiz, settings, new QuestionAnswer { BoolAnswer = false }).ScoredPoints);
        Assert.Equal(0, Grade(quiz, settings, new QuestionAnswer()).ScoredPoints);
    }

    // --- Short answer -------------------------------------------------------

    private static ShortAnswerQuestion ShortQuestion(bool caseSensitive = false)
    {
        var q = new ShortAnswerQuestion { Prompt = "Capital?", Points = 2, CaseSensitive = caseSensitive };
        q.AcceptedAnswers.Add("Paris");
        return q;
    }

    [Fact]
    public void ShortAnswerIgnoresCaseByDefault()
    {
        var settings = new QuizSettings();
        var quiz = Compile(settings, ShortQuestion());

        Assert.Equal(2, Grade(quiz, settings, new QuestionAnswer { TextAnswer = "PARIS" }).ScoredPoints);
    }

    [Fact]
    public void ShortAnswerTrimsWhitespace()
    {
        // A stray space is a keystroke, not a wrong answer.
        var settings = new QuizSettings();
        var quiz = Compile(settings, ShortQuestion());

        Assert.Equal(2, Grade(quiz, settings, new QuestionAnswer { TextAnswer = "  Paris  " }).ScoredPoints);
    }

    [Fact]
    public void ShortAnswerHonoursCaseSensitivity()
    {
        var settings = new QuizSettings();
        var quiz = Compile(settings, ShortQuestion(caseSensitive: true));

        Assert.Equal(0, Grade(quiz, settings, new QuestionAnswer { TextAnswer = "paris" }).ScoredPoints);
        Assert.Equal(2, Grade(quiz, settings, new QuestionAnswer { TextAnswer = "Paris" }).ScoredPoints);
    }

    [Fact]
    public void ShortAnswerAcceptsAnyAlternative()
    {
        var q = new ShortAnswerQuestion { Prompt = "?", Points = 2 };
        q.AcceptedAnswers.Add("Paris");
        q.AcceptedAnswers.Add("Paris, France");

        var settings = new QuizSettings();
        var quiz = Compile(settings, q);

        Assert.Equal(2, Grade(quiz, settings, new QuestionAnswer { TextAnswer = "Paris, France" }).ScoredPoints);
    }

    // --- Fill in the blank --------------------------------------------------

    private static FillInTheBlankQuestion BlankQuestion()
    {
        var q = new FillInTheBlankQuestion { Prompt = "The {{1}} is {{2}}", Points = 4 };
        q.Blanks.Add(new Blank { Ordinal = 1, AcceptedAnswers = { "cat", "feline" } });
        q.Blanks.Add(new Blank { Ordinal = 2, AcceptedAnswers = { "black" } });
        return q;
    }

    [Fact]
    public void BlanksScorePartially()
    {
        var settings = new QuizSettings();
        var quiz = Compile(settings, BlankQuestion());

        var result = Grade(quiz, settings,
            new QuestionAnswer { BlankAnswers = { [0] = "cat" } });

        Assert.Equal(2, result.ScoredPoints);
    }

    [Fact]
    public void EveryBlankRightScoresFull()
    {
        var settings = new QuizSettings();
        var quiz = Compile(settings, BlankQuestion());

        var result = Grade(quiz, settings,
            new QuestionAnswer { BlankAnswers = { [0] = "feline", [1] = "black" } });

        Assert.Equal(4, result.ScoredPoints);
    }

    [Fact]
    public void BlanksAreMatchedByPositionNotOrdinalValue()
    {
        // Ordinals are 1-based in the model, answers are 0-based by position.
        // Getting this wrong scores blank 2's answer against blank 1.
        var settings = new QuizSettings();
        var quiz = Compile(settings, BlankQuestion());

        var result = Grade(quiz, settings,
            new QuestionAnswer { BlankAnswers = { [0] = "black", [1] = "cat" } });

        Assert.Equal(0, result.ScoredPoints);
    }

    // --- Matching -----------------------------------------------------------

    private static MatchingQuestion MatchQuestion()
    {
        var q = new MatchingQuestion { Prompt = "Match", Points = 3 };
        q.Pairs.Add(new MatchPair { Left = "One", Right = "Uno" });
        q.Pairs.Add(new MatchPair { Left = "Two", Right = "Dos" });
        q.Pairs.Add(new MatchPair { Left = "Three", Right = "Tres" });
        q.Distractors.Add("Cuatro");
        return q;
    }

    [Fact]
    public void MatchingScoresPartially()
    {
        var settings = new QuizSettings();
        var quiz = Compile(settings, MatchQuestion());

        var result = Grade(quiz, settings,
            new QuestionAnswer { MatchAnswers = { [0] = "Uno", [1] = "Dos" } });

        Assert.Equal(2, result.ScoredPoints);
    }

    [Fact]
    public void ADistractorIsNeverCorrect()
    {
        var settings = new QuizSettings();
        var quiz = Compile(settings, MatchQuestion());

        var result = Grade(quiz, settings,
            new QuestionAnswer { MatchAnswers = { [0] = "Cuatro" } });

        Assert.Equal(0, result.ScoredPoints);
    }

    // --- Essays: the exclusion ---------------------------------------------

    [Fact]
    public void AnEssayIsNeverAutoGraded()
    {
        var settings = new QuizSettings();
        var quiz = Compile(settings, new EssayQuestion { Prompt = "Discuss", Points = 10 });

        var result = Grade(quiz, settings, new QuestionAnswer { EssayAnswer = "Some prose." });

        Assert.True(result.Results[0].NeedsReview);
        Assert.Null(result.Results[0].IsCorrect);
    }

    [Fact]
    public void AnEssayIsExcludedFromTheDenominatorNotCountedAsZero()
    {
        // The difference between honest and defamatory. A 10-point MC answered
        // perfectly beside a 10-point essay is 100% of what could be marked.
        // Counting the essay as zero would read 50% -- a fail at the default
        // bar -- for someone who got everything markable right.
        var settings = new QuizSettings();
        var quiz = Compile(settings,
            SingleQuestion(points: 10),
            new EssayQuestion { Prompt = "Discuss", Points = 10 });

        var compiled = quiz.Sections.SelectMany(s => s.Questions).ToList();
        var single = compiled.First(c => c.Question is MultipleChoiceSingleQuestion);
        var essay = compiled.First(c => c.Question is EssayQuestion);

        var result = Grader.Grade(quiz, new Dictionary<CompiledQuestion, QuestionAnswer>
        {
            [single] = new() { ChoiceIndex = 1 },
            [essay] = new() { EssayAnswer = "Some prose." },
        }, settings, TimeSpan.Zero, timedOut: false);

        Assert.Equal(10, result.AutoGradedPoints);      // not 20
        Assert.Equal(10, result.ScoredPoints);
        Assert.Equal(100, result.Percentage);
        Assert.True(result.Passed);
        Assert.Equal(10, result.PointsAwaitingReview);
        Assert.Equal(1, result.QuestionsAwaitingReview);
    }

    [Fact]
    public void AnAllEssayPaperHasNoAutomaticResultAtAll()
    {
        // Not zero: null. Showing 0% for a paper nobody has marked would be a
        // lie, and a Congratulations screen would be absurd.
        var settings = new QuizSettings();
        var quiz = Compile(settings,
            new EssayQuestion { Prompt = "One", Points = 10 },
            new EssayQuestion { Prompt = "Two", Points = 5 });

        var result = Grade(quiz, settings,
            new QuestionAnswer { EssayAnswer = "a" },
            new QuestionAnswer { EssayAnswer = "b" });

        Assert.Null(result.Percentage);
        Assert.Null(result.Passed);
        Assert.Equal(15, result.PointsAwaitingReview);
        Assert.True(result.HasReviewItems);
    }

    [Fact]
    public void ZeroPointQuestionsAreExcludedToo()
    {
        var settings = new QuizSettings();
        var quiz = Compile(settings, SingleQuestion(points: 10), SingleQuestion(points: 0));

        var compiled = quiz.Sections.SelectMany(s => s.Questions).ToList();

        var result = Grader.Grade(quiz, new Dictionary<CompiledQuestion, QuestionAnswer>
        {
            [compiled[0]] = new() { ChoiceIndex = 1 },
            [compiled[1]] = new() { ChoiceIndex = 0 },
        }, settings, TimeSpan.Zero, timedOut: false);

        Assert.Equal(10, result.AutoGradedPoints);
        Assert.Equal(100, result.Percentage);
    }

    // --- Pass mark ----------------------------------------------------------

    [Fact]
    public void ThePercentageAndTheVerdictNeverDisagree()
    {
        // These were computed two different ways once: the grader's own
        // percentage (essays excluded) and CompiledQuiz.PassesOnQuestions
        // (essays included, because it describes the PRINTED paper). The result
        // was a screen reading "100%" above the word FAIL. One percentage, one
        // comparison.
        var settings = new QuizSettings
        {
            PassMarkBasis = PassMarkBasis.QuestionCount,
            PassPercentage = 60,
        };

        var quiz = Compile(settings,
            SingleQuestion(points: 1),
            new EssayQuestion { Prompt = "Discuss", Points = 1 });

        var compiled = quiz.Sections.SelectMany(s => s.Questions).ToList();
        var single = compiled.First(c => c.Question is MultipleChoiceSingleQuestion);
        var essay = compiled.First(c => c.Question is EssayQuestion);

        var result = Grader.Grade(quiz, new Dictionary<CompiledQuestion, QuestionAnswer>
        {
            [single] = new() { ChoiceIndex = 1 },
            [essay] = new() { EssayAnswer = "prose" },
        }, settings, TimeSpan.Zero, timedOut: false);

        Assert.Equal(100, result.Percentage);
        Assert.True(result.Passed);
    }

    [Fact]
    public void PassMarkOnPointsUsesTheAutoGradedTotal()
    {
        var settings = new QuizSettings
        {
            PassMarkBasis = PassMarkBasis.TotalPoints,
            PassPercentage = 50,
        };

        var quiz = Compile(settings, BlankQuestion());   // 4 points, 2 blanks

        var result = Grade(quiz, settings,
            new QuestionAnswer { BlankAnswers = { [0] = "cat" } });   // half

        Assert.Equal(2, result.ScoredPoints);
        Assert.Equal(50, result.Percentage);
        Assert.True(result.Passed);       // exactly on the bar passes
    }

    [Fact]
    public void JustUnderTheBarFails()
    {
        var settings = new QuizSettings
        {
            PassMarkBasis = PassMarkBasis.TotalPoints,
            PassPercentage = 60,
        };

        var quiz = Compile(settings, BlankQuestion());

        var result = Grade(quiz, settings,
            new QuestionAnswer { BlankAnswers = { [0] = "cat" } });   // 50%

        Assert.False(result.Passed);
    }

    [Fact]
    public void QuestionCountBasisCountsQuestionsNotPoints()
    {
        var settings = new QuizSettings
        {
            PassMarkBasis = PassMarkBasis.QuestionCount,
            PassPercentage = 50,
        };

        // One 1-point question right, one 10-point question wrong.
        // By points that is 1/11 = 9%. By question count it is 1/2 = 50%.
        var quiz = Compile(settings, SingleQuestion(points: 1), SingleQuestion(points: 10));

        var compiled = quiz.Sections.SelectMany(s => s.Questions).ToList();
        var small = compiled.First(c => c.Question.Points == 1);
        var large = compiled.First(c => c.Question.Points == 10);

        var result = Grader.Grade(quiz, new Dictionary<CompiledQuestion, QuestionAnswer>
        {
            [small] = new() { ChoiceIndex = 1 },
            [large] = new() { ChoiceIndex = 0 },
        }, settings, TimeSpan.Zero, timedOut: false);

        Assert.Equal(50, result.Percentage);
        Assert.True(result.Passed);
    }

    // --- Reporting ----------------------------------------------------------

    [Fact]
    public void IncorrectQuestionsAreListedForReview()
    {
        var settings = new QuizSettings();
        var quiz = Compile(settings, SingleQuestion(), SingleQuestion());

        var compiled = quiz.Sections.SelectMany(s => s.Questions).ToList();

        var result = Grader.Grade(quiz, new Dictionary<CompiledQuestion, QuestionAnswer>
        {
            [compiled[0]] = new() { ChoiceIndex = 1 },   // right
            [compiled[1]] = new() { ChoiceIndex = 0 },   // wrong
        }, settings, TimeSpan.Zero, timedOut: false);

        Assert.Single(result.Incorrect);
    }

    [Fact]
    public void HalfMarksCountAsCorrect()
    {
        // The existing rule: a question is correct at half its points or more.
        var settings = new QuizSettings { PassMarkBasis = PassMarkBasis.QuestionCount };
        var quiz = Compile(settings, BlankQuestion());

        var result = Grade(quiz, settings,
            new QuestionAnswer { BlankAnswers = { [0] = "cat" } });   // 2 of 4

        Assert.True(result.Results[0].IsCorrect);
    }

    [Fact]
    public void TheTimeoutFlagIsCarried()
    {
        var settings = new QuizSettings();
        var quiz = Compile(settings, SingleQuestion());

        var compiled = quiz.Sections.SelectMany(s => s.Questions).ToList();

        var result = Grader.Grade(quiz,
            new Dictionary<CompiledQuestion, QuestionAnswer> { [compiled[0]] = new() },
            settings, TimeSpan.FromMinutes(30), timedOut: true);

        Assert.True(result.TimedOut);
        Assert.Equal(TimeSpan.FromMinutes(30), result.Elapsed);
    }

    [Fact]
    public void FractionalPointsSurvive()
    {
        var settings = new QuizSettings();
        var q = new FillInTheBlankQuestion { Prompt = "{{1}} {{2}}", Points = 2.5 };
        q.Blanks.Add(new Blank { Ordinal = 1, AcceptedAnswers = { "a" } });
        q.Blanks.Add(new Blank { Ordinal = 2, AcceptedAnswers = { "b" } });

        var quiz = Compile(settings, q);

        var result = Grade(quiz, settings, new QuestionAnswer { BlankAnswers = { [0] = "a" } });

        Assert.Equal(1.25, result.ScoredPoints);
    }
}
