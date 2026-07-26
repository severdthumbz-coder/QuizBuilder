using System.Globalization;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Flattens a graded attempt into something storable.
///
/// Answers become TEXT here, while the compiled paper is still in hand. Storing
/// a reference to the question instead would mean a report that changes when the
/// author edits the quiz -- fix a typo and last week's report silently shows the
/// new wording -- or throws when a question is deleted. A record of what
/// happened should say what was on screen at the time.
/// </summary>
public static class AttemptRecordBuilder
{
    public static AttemptRecord Build(Guid quizId, string quizTitle, AttemptResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new AttemptRecord
        {
            QuizId = quizId,
            QuizTitle = quizTitle,
            TakenAt = result.TakenAt,
            Percentage = result.Percentage,
            Passed = result.Passed,
            ScoredPoints = result.ScoredPoints,
            AutoGradedPoints = result.AutoGradedPoints,
            PointsAwaitingReview = result.PointsAwaitingReview,
            QuestionsAwaitingReview = result.QuestionsAwaitingReview,
            Elapsed = result.Elapsed,
            TimedOut = result.TimedOut,
            Questions = result.Results.Select(ToRecord).ToList(),
        };
    }

    private static AttemptQuestionRecord ToRecord(QuestionResult result) => new()
    {
        Number = result.Question.Number,
        Prompt = result.Question.Question.Prompt,
        GivenAnswer = DescribeGiven(result.Question.Question, result.Answer),
        CorrectAnswer = DescribeCorrect(result.Question.Question),
        Scored = result.Scored,
        Possible = result.Possible,
        IsCorrect = result.IsCorrect,
        NeedsReview = result.NeedsReview,
    };

    /// <summary>What the taker actually entered, as they would recognise it.</summary>
    private static string DescribeGiven(Question question, QuestionAnswer answer)
    {
        if (answer.IsEmpty) return string.Empty;

        switch (question)
        {
            case MultipleChoiceSingleQuestion q:
                return answer.ChoiceIndex is { } i && i >= 0 && i < q.Choices.Count
                    ? q.Choices[i].Text
                    : string.Empty;

            case MultipleChoiceMultipleQuestion q:
                return string.Join(", ", answer.ChoiceIndices
                    .Where(x => x >= 0 && x < q.Choices.Count)
                    .OrderBy(x => x)
                    .Select(x => q.Choices[x].Text));

            case TrueFalseQuestion:
                return answer.BoolAnswer switch { true => "True", false => "False", _ => string.Empty };

            case ShortAnswerQuestion:
                return answer.TextAnswer ?? string.Empty;

            case FillInTheBlankQuestion q:
                var blanks = q.Blanks.OrderBy(b => b.Ordinal).ToList();

                return string.Join(", ", blanks.Select((_, index) =>
                    answer.BlankAnswers.TryGetValue(index, out var given) && !string.IsNullOrWhiteSpace(given)
                        ? $"{index + 1}: {given}"
                        : $"{index + 1}: —"));

            case MatchingQuestion q:
                return string.Join(", ", q.Pairs.Select((pair, index) =>
                    answer.MatchAnswers.TryGetValue(index, out var given) && !string.IsNullOrWhiteSpace(given)
                        ? $"{pair.Left} → {given}"
                        : $"{pair.Left} → —"));

            case EssayQuestion:
                return answer.EssayAnswer ?? string.Empty;

            case SequenceQuestion q:
                // The arrangement the taker chose, in their order. Indices are
                // bounds-checked because a saved attempt may predate an edit
                // that shortened the item list.
                return string.Join(" → ", answer.SequenceAnswer
                    .Where(i => i >= 0 && i < q.Items.Count)
                    .Select(i => q.Items[i]));

            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// What would have been right. Empty for an essay -- there is no single
    /// correct answer, and inventing one would be the whole reason essays are
    /// excluded from grading in the first place.
    /// </summary>
    // The per-type describer now lives in AnswerDescriber, shared with the flash
    // cards. One describer, so the review screen and a flash card can never
    // disagree about the same question's answer.
    private static string DescribeCorrect(Question question) => AnswerDescriber.Describe(question);

    /// <summary>A score like "7.5 / 10", trimmed of pointless decimals.</summary>
    public static string FormatPoints(double value) =>
        value == Math.Floor(value)
            ? value.ToString("0", CultureInfo.CurrentCulture)
            : value.ToString("0.##", CultureInfo.CurrentCulture);
}
