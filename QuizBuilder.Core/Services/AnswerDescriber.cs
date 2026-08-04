using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Describes the correct answer to a question as text, per type.
///
/// Extracted so the results report and the flash cards share ONE describer.
/// They both need "what is the right answer here", and a second copy would be a
/// slow-motion bug: fix a format in one place, forget the other, and the review
/// screen and the flash card disagree about the same question.
/// </summary>
public static class AnswerDescriber
{
    /// <summary>
    /// The correct answer as a single string.
    ///
    /// Empty for an essay: there is no single correct answer, and inventing one
    /// would undo the whole reason essays are excluded from grading. Callers that
    /// want to show something for an essay (the flash cards do) check for empty
    /// and fall back to the rubric.
    /// </summary>
    public static string Describe(Question question)
    {
        switch (question)
        {
            case MultipleChoiceSingleQuestion q:
                return q.Choices.FirstOrDefault(c => c.IsCorrect)?.Text ?? string.Empty;

            case MultipleChoiceMultipleQuestion q:
                return string.Join(", ", q.Choices.Where(c => c.IsCorrect).Select(c => c.Text));

            case TrueFalseQuestion q:
                return q.CorrectAnswer ? "True" : "False";

            case ShortAnswerQuestion q:
                return string.Join(" / ", q.AcceptedAnswers);

            case FillInTheBlankQuestion q:
                return string.Join(", ", q.Blanks
                    .OrderBy(b => b.Ordinal)
                    .Select((b, index) => $"{index + 1}: {string.Join(" / ", b.AcceptedAnswers)}"));

            case MatchingQuestion q:
                return string.Join(", ", q.Pairs.Select(p => $"{p.Left} → {p.Right}"));

            case SequenceQuestion q:
                // Items are stored in the correct order, so the answer is just
                // the list read out. The arrow matches the matching format, so
                // both read as "this, then this".
                return string.Join(" → ", q.Items);

            case NumericQuestion q:
                return q.Tolerance > 0
                    ? $"{q.Target} (± {q.Tolerance})" + (string.IsNullOrWhiteSpace(q.Unit) ? "" : $" {q.Unit}")
                    : q.Target.ToString(System.Globalization.CultureInfo.InvariantCulture)
                      + (string.IsNullOrWhiteSpace(q.Unit) ? "" : $" {q.Unit}");

            case DropdownQuestion q:
                return q.Choices.FirstOrDefault(c => c.IsCorrect)?.Text ?? string.Empty;

            default:
                return string.Empty;
        }
    }
}
