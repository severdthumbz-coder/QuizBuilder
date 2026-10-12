using System.Globalization;
using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Turns a question into a header-keyed set of cell values, following
/// <see cref="QuizSheetSchema"/>. This is the single source of truth for "how a
/// question is laid out in a row", shared by the .xlsx exporter and the CSV
/// template writer so the two can never drift -- a drift that already bit once,
/// when a type-label switch and a body-writer switch disagreed about Numeric and
/// Dropdown.
///
/// Returns values by HEADER NAME (not column index). The caller places them into
/// whatever grid it is building; the importer reads them back by header name
/// too, so column order never matters.
/// </summary>
public static class QuizRowProjector
{
    /// <summary>The canonical type label for a question (the "Type" cell).</summary>
    public static string TypeName(Question question) => question switch
    {
        MultipleChoiceSingleQuestion => "MultipleChoiceSingle",
        MultipleChoiceMultipleQuestion => "MultipleChoiceMultiple",
        TrueFalseQuestion => "TrueFalse",
        ShortAnswerQuestion => "ShortAnswer",
        FillInTheBlankQuestion => "FillInTheBlank",
        MatchingQuestion => "Matching",
        EssayQuestion => "Essay",
        SequenceQuestion => "Sequence",
        NumericQuestion => "Numeric",
        DropdownQuestion => "Dropdown",

        // Degrade an unknown kind to Essay: the importer treats Essay as
        // hand-graded, so it becomes "needs a human" rather than silently wrong.
        _ => "Essay",
    };

    /// <summary>
    /// The non-empty cell values for one question, keyed by schema header.
    /// Points is included as an invariant-culture number string; empty fields
    /// are simply absent from the map.
    /// </summary>
    public static Dictionary<string, string> ToCells(Question question, string sectionTitle)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [QuizSheetSchema.Section] = sectionTitle,
            [QuizSheetSchema.Type] = TypeName(question),
            [QuizSheetSchema.Prompt] = question.Prompt,
            [QuizSheetSchema.Points] = question.Points.ToString("R", CultureInfo.InvariantCulture),
            [QuizSheetSchema.Hint] = question.Hint ?? string.Empty,
        };

        switch (question)
        {
            case MultipleChoiceSingleQuestion q:
                AppendChoices(values, q.Choices);
                break;

            case MultipleChoiceMultipleQuestion q:
                AppendChoices(values, q.Choices);
                break;

            case TrueFalseQuestion q:
                values[QuizSheetSchema.Correct(1)] = q.CorrectAnswer ? "TRUE" : "FALSE";
                break;

            case ShortAnswerQuestion q:
                for (var i = 0; i < Math.Min(q.AcceptedAnswers.Count, QuizSheetSchema.MaxOptions); i++)
                    values[QuizSheetSchema.Option(i + 1)] = q.AcceptedAnswers[i];
                break;

            case FillInTheBlankQuestion q:
                var ordered = q.Blanks.OrderBy(b => b.Ordinal).ToList();
                for (var i = 0; i < Math.Min(ordered.Count, QuizSheetSchema.MaxOptions); i++)
                    values[QuizSheetSchema.Option(i + 1)] = ordered[i].AcceptedAnswers.FirstOrDefault() ?? string.Empty;
                break;

            case MatchingQuestion q:
                for (var i = 0; i < Math.Min(q.Pairs.Count, QuizSheetSchema.MaxOptions); i++)
                {
                    values[QuizSheetSchema.Option(i + 1)] = q.Pairs[i].Left;
                    values[QuizSheetSchema.Match(i + 1)] = q.Pairs[i].Right;
                }

                for (var i = 0; i < Math.Min(q.Distractors.Count, QuizSheetSchema.MaxDistractors); i++)
                    values[QuizSheetSchema.Distractor(i + 1)] = q.Distractors[i];
                break;

            case SequenceQuestion q:
                for (var i = 0; i < Math.Min(q.Items.Count, QuizSheetSchema.MaxOptions); i++)
                    values[QuizSheetSchema.Option(i + 1)] = q.Items[i];
                break;

            case EssayQuestion q:
                values[QuizSheetSchema.Extra] = q.RubricNotes ?? string.Empty;
                break;

            case DropdownQuestion q:
                AppendChoices(values, q.Choices);
                break;

            case NumericQuestion q:
                values[QuizSheetSchema.Option(1)] = q.Target.ToString(CultureInfo.InvariantCulture);
                values[QuizSheetSchema.Option(2)] = q.Tolerance.ToString(CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(q.Unit))
                    values[QuizSheetSchema.Extra] = q.Unit;
                break;
        }

        return values;
    }

    private static void AppendChoices(Dictionary<string, string> values, List<Choice> choices)
    {
        for (var i = 0; i < Math.Min(choices.Count, QuizSheetSchema.MaxOptions); i++)
        {
            values[QuizSheetSchema.Option(i + 1)] = choices[i].Text;

            if (choices[i].IsCorrect)
                values[QuizSheetSchema.Correct(i + 1)] = "TRUE";
        }
    }
}
