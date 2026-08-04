using System.Globalization;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Marks answers against a compiled paper.
///
/// Every rule here was modelled and run before it was written, because scoring
/// is the one part of this app where being subtly wrong is invisible: a grader
/// that is 10% too generous produces plausible numbers forever.
/// </summary>
public sealed class QuizGrader : IQuizGrader
{
    public AttemptResult Grade(
        CompiledQuiz quiz,
        IReadOnlyDictionary<CompiledQuestion, QuestionAnswer> answers,
        QuizSettings settings,
        TimeSpan elapsed,
        bool timedOut)
    {
        ArgumentNullException.ThrowIfNull(quiz);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(settings);

        var results = new List<QuestionResult>();

        foreach (var compiled in quiz.Sections.SelectMany(s => s.Questions))
        {
            var answer = answers.TryGetValue(compiled, out var given) ? given : new QuestionAnswer();
            var needsReview = compiled.Question is EssayQuestion;

            results.Add(new QuestionResult
            {
                Question = compiled,
                Answer = answer,
                Possible = compiled.Question.Points,
                NeedsReview = needsReview,

                // An essay scores nothing because it is not scored at all --
                // NeedsReview is what carries that, and the totals below skip it.
                Scored = needsReview ? 0 : Score(compiled.Question, answer),
            });
        }

        // Essays and 0-point questions are excluded from the denominator, not
        // counted as zero. The difference decides pass/fail: a 10-point MC
        // answered perfectly next to a 10-point essay is 100% of what could be
        // marked, and would be 50% -- a fail at the default bar -- if the essay
        // were treated as a zero.
        var auto = results.Where(r => !r.NeedsReview && r.Possible > 0).ToList();

        var autoPossible = auto.Sum(r => r.Possible);
        var autoScored = auto.Sum(r => r.Scored);

        var review = results.Where(r => r.NeedsReview).ToList();

        double? percentage = null;
        bool? passed = null;

        if (auto.Count > 0 && autoPossible > 0)
        {
            percentage = settings.PassMarkBasis == PassMarkBasis.QuestionCount
                ? PercentageByQuestionCount(auto)
                : autoScored / autoPossible * 100;

            // Pass/fail comes from THAT percentage, not from CompiledQuiz's
            // Passes* helpers.
            //
            // Tempting as reuse was, those answer a different question. They
            // count every question with points > 0 as gradeable, essays
            // included, because they exist to state the pass mark on a PRINTED
            // paper -- where the essay is a real question the student can see.
            // This grader excludes essays. Feeding one's numbers to the other
            // produces a screen reading "100%" above the word FAIL: correct on
            // 1 of 1 markable questions, but 1 of 2 by the printed rule.
            //
            // One percentage, one comparison. Reusing a rule that answers a
            // different question is not reuse.
            passed = percentage >= quiz.PassPercentage;
        }

        return new AttemptResult
        {
            Results = results,
            ScoredPoints = autoScored,
            AutoGradedPoints = autoPossible,
            PointsAwaitingReview = review.Sum(r => r.Possible),
            QuestionsAwaitingReview = review.Count,
            Percentage = percentage,
            Passed = passed,
            Elapsed = elapsed,
            TimedOut = timedOut,
            TakenAt = DateTimeOffset.Now,
        };
    }

    /// <summary>
    /// The question-count percentage: how many were correct, not how many points.
    /// </summary>
    private static double PercentageByQuestionCount(List<QuestionResult> auto)
    {
        if (auto.Count == 0) return 0;

        var correct = auto.Count(r => r.IsCorrect == true);

        return (double)correct / auto.Count * 100;
    }

    // --- Per-type rules -----------------------------------------------------

    private static double Score(Question question, QuestionAnswer answer) => question switch
    {
        MultipleChoiceSingleQuestion q => ScoreSingle(q, answer),
        MultipleChoiceMultipleQuestion q => ScoreMultiple(q, answer),
        TrueFalseQuestion q => ScoreTrueFalse(q, answer),
        ShortAnswerQuestion q => ScoreShortAnswer(q, answer),
        FillInTheBlankQuestion q => ScoreBlanks(q, answer),
        MatchingQuestion q => ScoreMatching(q, answer),
        SequenceQuestion q => ScoreSequence(q, answer),
        NumericQuestion q => ScoreNumeric(q, answer),
        DropdownQuestion q => ScoreDropdown(q, answer),
        _ => 0,
    };

    /// <summary>
    /// Numeric: parse the typed answer (invariant culture) and award full points
    /// when it is within tolerance of the target, inclusive. Blank or non-numeric
    /// input scores zero. A negative tolerance is clamped to zero so an author
    /// slip can never widen the window. inf/nan are rejected as answers. Proved
    /// in tools/port/numeric_grading_port.py.
    /// </summary>
    private static double ScoreNumeric(NumericQuestion question, QuestionAnswer answer)
    {
        if (string.IsNullOrWhiteSpace(answer.TextAnswer))
            return 0;

        if (!double.TryParse(
                answer.TextAnswer.Trim(),
                NumberStyles.Float | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var value))
            return 0;

        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0;

        var tolerance = question.Tolerance > 0 ? question.Tolerance : 0;

        return Math.Abs(value - question.Target) <= tolerance
            ? question.Points
            : 0;
    }

    /// <summary>
    /// Dropdown: identical scoring to single-choice multiple choice — one chosen
    /// index, correct when that choice is the correct one. Shares the logic so it
    /// can never drift from single-choice.
    /// </summary>
    private static double ScoreDropdown(DropdownQuestion question, QuestionAnswer answer)
    {
        if (answer.ChoiceIndex is not { } index) return 0;
        if (index < 0 || index >= question.Choices.Count) return 0;

        return question.Choices[index].IsCorrect ? question.Points : 0;
    }

    private static double ScoreSingle(MultipleChoiceSingleQuestion question, QuestionAnswer answer)
    {
        if (answer.ChoiceIndex is not { } index) return 0;
        if (index < 0 || index >= question.Choices.Count) return 0;

        return question.Choices[index].IsCorrect ? question.Points : 0;
    }

    /// <summary>
    /// Multiple-answer scoring.
    ///
    /// With partial credit the rule is (hits - misses) / correctCount, floored
    /// at zero. The obvious alternative -- hits / correctCount -- awards full
    /// marks for ticking every box, which makes the question worthless. Flooring
    /// at zero rather than allowing negatives keeps one bad question from eating
    /// marks earned elsewhere.
    /// </summary>
    private static double ScoreMultiple(MultipleChoiceMultipleQuestion question, QuestionAnswer answer)
    {
        var correct = question.Choices
            .Select((c, i) => (c, i))
            .Where(x => x.c.IsCorrect)
            .Select(x => x.i)
            .ToHashSet();

        var picked = answer.ChoiceIndices;

        if (correct.Count == 0) return 0;

        if (!question.AllowPartialCredit)
            return picked.SetEquals(correct) ? question.Points : 0;

        var hits = picked.Count(correct.Contains);
        var misses = picked.Count(p => !correct.Contains(p));

        var fraction = (hits - misses) / (double)correct.Count;

        return Math.Max(0, fraction) * question.Points;
    }

    private static double ScoreTrueFalse(TrueFalseQuestion question, QuestionAnswer answer)
        => answer.BoolAnswer == question.CorrectAnswer ? question.Points : 0;

    private static double ScoreShortAnswer(ShortAnswerQuestion question, QuestionAnswer answer)
    {
        if (string.IsNullOrWhiteSpace(answer.TextAnswer)) return 0;

        return Matches(answer.TextAnswer, question.AcceptedAnswers, question.CaseSensitive)
            ? question.Points
            : 0;
    }

    /// <summary>
    /// Blanks are partial by nature: each is independent, so the score is the
    /// fraction right.
    /// </summary>
    private static double ScoreBlanks(FillInTheBlankQuestion question, QuestionAnswer answer)
    {
        var ordered = question.Blanks.OrderBy(b => b.Ordinal).ToList();
        if (ordered.Count == 0) return 0;

        var hits = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            if (!answer.BlankAnswers.TryGetValue(i, out var given)) continue;
            if (string.IsNullOrWhiteSpace(given)) continue;

            if (Matches(given, ordered[i].AcceptedAnswers, question.CaseSensitive)) hits++;
        }

        return (double)hits / ordered.Count * question.Points;
    }

    private static double ScoreMatching(MatchingQuestion question, QuestionAnswer answer)
    {
        if (question.Pairs.Count == 0) return 0;

        var hits = 0;

        for (var i = 0; i < question.Pairs.Count; i++)
        {
            if (!answer.MatchAnswers.TryGetValue(i, out var given)) continue;

            // Ordinal: these are values picked from a list, not typed, so any
            // difference is a real difference. Distractors match nothing by
            // construction -- they are not any pair's Right.
            if (string.Equals(given, question.Pairs[i].Right, StringComparison.Ordinal)) hits++;
        }

        return (double)hits / question.Pairs.Count * question.Points;
    }

    /// <summary>
    /// Sequence scoring: credit per correctly-ordered adjacent pair.
    ///
    /// <para>
    /// A sequence question tests what follows what, so the transitions are the
    /// thing being assessed. Scoring by absolute position instead would give
    /// zero to someone who moved a single item to the wrong end while getting
    /// every other relative order right -- as harsh as random guessing for one
    /// mistake.
    /// </para>
    ///
    /// <para>
    /// Indices rather than item text, so duplicate items stay distinguishable.
    /// An answer that is not a permutation of the items cannot be scored
    /// positionally at all, so it scores zero rather than being guessed at.
    /// </para>
    /// </summary>
    private static double ScoreSequence(SequenceQuestion question, QuestionAnswer answer)
    {
        var n = question.Items.Count;

        // No transitions exist below two items. A lone item is trivially in
        // order; an empty question is unanswerable.
        if (n < 2)
            return n == 1 && answer.SequenceAnswer.Count == 1 && answer.SequenceAnswer[0] == 0
                ? question.Points
                : 0;

        var given = answer.SequenceAnswer;
        if (given.Count != n) return 0;

        // Must be a permutation of 0..n-1: duplicates or out-of-range indices
        // mean the answer does not describe an arrangement of these items.
        var seen = new bool[n];
        foreach (var index in given)
        {
            if (index < 0 || index >= n) return 0;
            if (seen[index]) return 0;
            seen[index] = true;
        }

        var hits = 0;
        for (var i = 0; i < n - 1; i++)
            if (given[i] + 1 == given[i + 1]) hits++;

        return (double)hits / (n - 1) * question.Points;
    }

    /// <summary>
    /// Text matching for the typed types.
    ///
    /// Trims always: " Paris " and "Paris" differ by a keystroke nobody meant,
    /// and marking that wrong would be pedantry rather than assessment. Case is
    /// the author's choice, per question.
    /// </summary>
    private static bool Matches(string given, IEnumerable<string> accepted, bool caseSensitive)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var trimmed = given.Trim();

        return accepted.Any(a => string.Equals(trimmed, a?.Trim(), comparison));
    }
}
