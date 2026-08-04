using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Sequence questions score by adjacent pairs: credit per correctly-ordered
/// neighbouring pair rather than per item in its absolute slot.
///
/// <para>
/// The rule was chosen against a reference model before any of this existed.
/// Scoring by absolute position gives zero to someone who moved a single item
/// to the wrong end while getting every other relative order right -- as harsh
/// as random guessing for one mistake. These tests pin the chosen behaviour,
/// including that case.
/// </para>
/// </summary>
public class SequenceQuestionTests
{
    private static SequenceQuestion Question(int itemCount, double points = 1)
    {
        var q = new SequenceQuestion { Points = points };
        for (var i = 0; i < itemCount; i++) q.Items.Add($"Item {i}");
        return q;
    }

    private static QuestionAnswer Answer(params int[] order)
    {
        var a = new QuestionAnswer();
        a.SequenceAnswer.AddRange(order);
        return a;
    }

    /// <summary>
    /// Scores one sequence question through the real compile-and-grade
    /// pipeline, returning the fraction of the question's points awarded.
    /// Going through the public API rather than reaching for the private
    /// scorer means these tests would catch a break anywhere along the path,
    /// not just in the arithmetic.
    /// </summary>
    private static double Score(SequenceQuestion q, QuestionAnswer a)
    {
        var settings = new QuizSettings { RandomizeAnswerOrder = false };

        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "S" };
        section.Questions.Add(q);
        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);

        var quiz = new QuizCompiler().Compile(doc, settings, seed: 1);
        var compiled = quiz.Sections.SelectMany(s => s.Questions).ToList();

        var map = new Dictionary<CompiledQuestion, QuestionAnswer>();
        if (compiled.Count > 0) map[compiled[0]] = a;

        var result = new QuizGrader()
            .Grade(quiz, map, settings, TimeSpan.FromMinutes(1), timedOut: false);

        var earned = result.Results.Sum(r => r.Scored);

        // Normalised so the expected values read as fractions of the question.
        return q.Points == 0 ? 0 : earned / q.Points;
    }

    [Fact]
    public void PerfectOrderScoresFull()
    {
        Assert.Equal(1.0, Score(Question(5), Answer(0, 1, 2, 3, 4)), 6);
    }

    [Theory]
    // Two of four transitions survive a single adjacent swap.
    [InlineData(new[] { 0, 1, 2, 4, 3 }, 0.5)]
    [InlineData(new[] { 1, 0, 2, 3, 4 }, 0.5)]
    // Moving one item to the far end keeps three of four transitions.
    [InlineData(new[] { 1, 2, 3, 4, 0 }, 0.75)]
    [InlineData(new[] { 4, 0, 1, 2, 3 }, 0.75)]
    // Reversing breaks every transition.
    [InlineData(new[] { 4, 3, 2, 1, 0 }, 0.0)]
    // Two separate swaps break all four.
    [InlineData(new[] { 1, 0, 3, 2, 4 }, 0.0)]
    public void PartialCreditFollowsAdjacentPairs(int[] given, double expected)
    {
        Assert.Equal(expected, Score(Question(5), Answer(given)), 6);
    }

    [Fact]
    public void MovingOneItemToTheEndIsNotTreatedAsTotalFailure()
    {
        // The case that rules out absolute-position scoring: every relative
        // order is right except the one moved item.
        var score = Score(Question(5), Answer(1, 2, 3, 4, 0));

        Assert.True(score > 0.5, $"expected meaningful partial credit, got {score}");
    }

    [Fact]
    public void ScoreScalesWithQuestionPoints()
    {
        // Three of four transitions is 0.75 of the question, whatever it is
        // worth: 6 points out of 8.
        Assert.Equal(0.75, Score(Question(5, points: 8), Answer(1, 2, 3, 4, 0)), 6);
    }

    [Fact]
    public void UnansweredScoresZero()
    {
        Assert.Equal(0.0, Score(Question(5), Answer()), 6);
    }

    [Fact]
    public void PartialAnswerScoresZero()
    {
        // Fewer indices than items does not describe an arrangement.
        Assert.Equal(0.0, Score(Question(5), Answer(0, 1, 2)), 6);
    }

    [Theory]
    [InlineData(new[] { 0, 0, 1, 2, 3 })]   // duplicate index
    [InlineData(new[] { 0, 1, 2, 3, 9 })]   // out of range
    [InlineData(new[] { -1, 1, 2, 3, 4 })]  // negative
    public void MalformedAnswersScoreZeroRatherThanThrowing(int[] given)
    {
        Assert.Equal(0.0, Score(Question(5), Answer(given)), 6);
    }

    [Fact]
    public void SingleItemIsTriviallyCorrect()
    {
        // No transitions exist, and one item is always in order.
        Assert.Equal(1.0, Score(Question(1), Answer(0)), 6);
    }

    [Fact]
    public void EmptyQuestionScoresZero()
    {
        Assert.Equal(0.0, Score(Question(0), Answer()), 6);
    }

    [Fact]
    public void TwoItemsAreAllOrNothing()
    {
        // With one transition there is no middle ground, which is correct.
        Assert.Equal(1.0, Score(Question(2), Answer(0, 1)), 6);
        Assert.Equal(0.0, Score(Question(2), Answer(1, 0)), 6);
    }

    [Fact]
    public void DuplicateItemTextDoesNotBreakScoring()
    {
        // Scoring is on indices, so items reading the same stay distinct.
        // Scoring on the text would collapse them and mark a correct answer
        // wrong.
        var q = new SequenceQuestion { Points = 1 };
        q.Items.AddRange(new[] { "Repeat", "Repeat", "Repeat", "Repeat" });

        Assert.Equal(1.0, Score(q, Answer(0, 1, 2, 3)), 6);
    }

    [Fact]
    public void EveryPermutationScoresWithinRangeAndOnlyTheCorrectOneScoresFull()
    {
        var q = Question(5);

        foreach (var permutation in Permutations(new[] { 0, 1, 2, 3, 4 }))
        {
            var score = Score(q, Answer(permutation));

            Assert.InRange(score, 0.0, 1.0);

            var isCorrectOrder = permutation.SequenceEqual(new[] { 0, 1, 2, 3, 4 });
            Assert.Equal(isCorrectOrder, score == 1.0);
        }
    }

    [Fact]
    public void ItemsAreStoredInTheCorrectOrder()
    {
        // The model holds the answer key. Anything that shuffles Items in place
        // destroys the thing the grader compares against.
        var q = Question(3);

        Assert.Equal(new[] { "Item 0", "Item 1", "Item 2" }, q.Items);
    }

    [Fact]
    public void CloneCopiesItemsAndDoesNotShareTheList()
    {
        var q = Question(3);
        var copy = (SequenceQuestion)q.Clone();

        Assert.Equal(q.Items, copy.Items);
        Assert.NotSame(q.Items, copy.Items);

        copy.Items.Add("Extra");
        Assert.Equal(3, q.Items.Count);
    }

    [Fact]
    public void CloneGivesTheCopyItsOwnIdentity()
    {
        var q = Question(3);
        var copy = q.Clone();

        Assert.NotEqual(q.Id, copy.Id);
    }

    [Fact]
    public void DescribesTheCorrectOrderAsTheAnswer()
    {
        var q = Question(3);

        Assert.Equal("Item 0 → Item 1 → Item 2", AnswerDescriber.Describe(q));
    }

    [Fact]
    public void KindAndDisplayNameAreSet()
    {
        var q = new SequenceQuestion();

        Assert.Equal(QuestionKind.Sequence, q.Kind);
        Assert.Equal("Sequence", q.KindDisplayName);
    }

    [Fact]
    public void ExistingKindValuesAreStableAndNewOnesAreAppended()
    {
        // The kind is persisted numerically in some spreadsheet round-trips, so
        // an existing value's number must never change — inserting a new kind
        // earlier would renumber the ones after it and silently rewrite existing
        // files. This pins each established value to its number; new types must
        // be APPENDED with the next number, never inserted. When you add a type,
        // add its assertion at the end here — that is the deliberate checkpoint.
        Assert.Equal(0, (int)QuestionKind.MultipleChoiceSingle);
        Assert.Equal(1, (int)QuestionKind.MultipleChoiceMultiple);
        Assert.Equal(2, (int)QuestionKind.TrueFalse);
        Assert.Equal(3, (int)QuestionKind.ShortAnswer);
        Assert.Equal(4, (int)QuestionKind.FillInTheBlank);
        Assert.Equal(5, (int)QuestionKind.Matching);
        Assert.Equal(6, (int)QuestionKind.Essay);
        Assert.Equal(7, (int)QuestionKind.Sequence);

        // v3 additions — appended after Sequence.
        Assert.Equal(8, (int)QuestionKind.Numeric);
        Assert.Equal(9, (int)QuestionKind.Dropdown);

        // And the newest type is genuinely last, so nothing was inserted after it.
        Assert.Equal(QuestionKind.Dropdown, Enum.GetValues<QuestionKind>()[^1]);
    }

    [Fact]
    public void AnAnswerWithNoSequenceIsEmpty()
    {
        Assert.True(new QuestionAnswer().IsEmpty);
        Assert.False(Answer(0, 1).IsEmpty);
    }

    // --- Results screen (AttemptRecordBuilder) -------------------------------
    //
    // What the student sees after grading: their arrangement rendered as item
    // text, and the correct arrangement beside it. This drives the same
    // compile-and-grade pipeline as the scoring tests, then builds the record
    // the results view binds to, so a break in the index-to-text mapping shows
    // up here rather than only in front of a user.

    private static AttemptQuestionRecord RecordFor(SequenceQuestion q, QuestionAnswer a)
    {
        var settings = new QuizSettings { RandomizeAnswerOrder = false };

        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "S" };
        section.Questions.Add(q);
        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);

        var quiz = new QuizCompiler().Compile(doc, settings, seed: 1);
        var compiled = quiz.Sections.SelectMany(s => s.Questions).ToList();

        var map = new Dictionary<CompiledQuestion, QuestionAnswer>();
        if (compiled.Count > 0) map[compiled[0]] = a;

        var result = new QuizGrader()
            .Grade(quiz, map, settings, TimeSpan.FromMinutes(1), timedOut: false);

        var record = AttemptRecordBuilder.Build(Guid.NewGuid(), "T", result);
        return record.Questions.Single();
    }

    [Fact]
    public void ResultsShowTheTakersArrangementAsItemText()
    {
        // Taker put them in a wrong order; the record shows that order in words,
        // not raw indices.
        var record = RecordFor(Question(4), Answer(2, 0, 3, 1));

        Assert.Equal("Item 2 → Item 0 → Item 3 → Item 1", record.GivenAnswer);
    }

    [Fact]
    public void ResultsShowTheCorrectOrderBesideTheTakers()
    {
        var record = RecordFor(Question(4), Answer(2, 0, 3, 1));

        Assert.Equal("Item 0 → Item 1 → Item 2 → Item 3", record.CorrectAnswer);
    }

    [Fact]
    public void ResultsForAnUntouchedSequenceShowNoGivenAnswer()
    {
        // No drag means an empty answer, which must render as nothing rather
        // than as a spurious "correct" arrangement.
        var record = RecordFor(Question(4), new QuestionAnswer());

        Assert.Equal(string.Empty, record.GivenAnswer);
    }

    [Fact]
    public void ResultsIgnoreOutOfRangeIndicesFromAStaleAttempt()
    {
        // A saved attempt can outlive an edit that shortened the item list, so
        // an index past the end must be dropped rather than throw or show junk.
        var q = Question(3);
        var answer = Answer(0, 1, 2, 9);

        var record = RecordFor(q, answer);

        Assert.Equal("Item 0 → Item 1 → Item 2", record.GivenAnswer);
    }

    private static IEnumerable<int[]> Permutations(int[] source)
    {
        if (source.Length <= 1)
        {
            yield return source;
            yield break;
        }

        for (var i = 0; i < source.Length; i++)
        {
            var rest = source.Take(i).Concat(source.Skip(i + 1)).ToArray();
            foreach (var tail in Permutations(rest))
                yield return new[] { source[i] }.Concat(tail).ToArray();
        }
    }
}
