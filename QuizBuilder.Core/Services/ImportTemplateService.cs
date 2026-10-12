using System.Text;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Builds a "both" import template: a worked-example section plus the currently
/// loaded quiz's questions, in the exact layout <see cref="ExcelImporter"/>
/// reads back.
///
/// The xlsx path reuses the real exporter, so the template is byte-identical in
/// shape to an exported quiz (and inherits its Guide sheet). The csv path uses
/// the shared <see cref="QuizRowProjector"/>, so the same column rules apply and
/// the file imports cleanly through the CSV reader.
///
/// CSV write proven in tools/port/csv_import_template_port.py.
/// </summary>
public sealed class ImportTemplateService : IImportTemplateService
{
    private readonly IExcelExporter _excel;

    /// <summary>The example section's title. Named so a user knows to delete it.</summary>
    public const string ExampleSectionTitle = "Example questions (delete this section)";

    public ImportTemplateService(IExcelExporter excel)
    {
        _excel = excel ?? throw new ArgumentNullException(nameof(excel));
    }

    public QuizDocument BuildTemplateDocument(QuizDocument currentQuiz)
    {
        ArgumentNullException.ThrowIfNull(currentQuiz);

        var doc = new QuizDocument { Title = "Import template" };

        // 1) The teaching section: one illustrative question per common type.
        var examples = new Section { Title = ExampleSectionTitle };
        foreach (var q in ExampleQuestions())
            examples.Questions.Add(q);

        doc.Sections.Add(examples);
        doc.SectionDisplayOrder.Add(examples.Id);

        // 2) The user's own quiz, cloned so editing the template never touches
        //    the live document. Clone() gives each question a fresh id, which is
        //    correct: these are a copy for a template, not the same objects.
        foreach (var section in currentQuiz.SectionsInDisplayOrder())
        {
            if (section.Questions.Count == 0) continue;   // nothing to illustrate

            var copy = section.Clone();
            doc.Sections.Add(copy);
            doc.SectionDisplayOrder.Add(copy.Id);
        }

        return doc;
    }

    public void WriteXlsx(Stream stream, QuizDocument currentQuiz)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _excel.Write(stream, BuildTemplateDocument(currentQuiz));
    }

    public void WriteCsv(Stream stream, QuizDocument currentQuiz)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var doc = BuildTemplateDocument(currentQuiz);
        var headers = QuizSheetSchema.Headers;

        var grid = new List<IReadOnlyList<string>>
        {
            headers,   // header row
        };

        foreach (var section in doc.SectionsInDisplayOrder())
        {
            foreach (var question in section.Questions)
            {
                var cells = QuizRowProjector.ToCells(question, section.Title);

                var row = new string[headers.Count];
                for (var i = 0; i < headers.Count; i++)
                    row[i] = cells.TryGetValue(headers[i], out var v) ? v : string.Empty;

                grid.Add(row);
            }
        }

        var text = CsvGrid.Write(grid);

        // Write a UTF-8 BOM: Excel opens a BOM-less CSV as the system codepage
        // and mangles any non-ASCII prompt. The importer's StreamReader detects
        // and skips the BOM, so the round-trip is unaffected.
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        writer.Write(text);
    }

    /// <summary>
    /// One illustrative question per common type. Kept small and recognisable:
    /// the point is to show the layout, not to be a real quiz. Every example is
    /// a legal question so the template imports with zero problems if left as-is.
    /// </summary>
    private static IEnumerable<Question> ExampleQuestions()
    {
        yield return new MultipleChoiceSingleQuestion
        {
            Prompt = "What is the capital of France?",
            Points = 1,
            Choices =
            {
                new Choice { Text = "Paris", IsCorrect = true },
                new Choice { Text = "London" },
                new Choice { Text = "Berlin" },
                new Choice { Text = "Madrid" },
            },
        };

        yield return new MultipleChoiceMultipleQuestion
        {
            Prompt = "Which of these are prime numbers?",
            Points = 2,
            Choices =
            {
                new Choice { Text = "2", IsCorrect = true },
                new Choice { Text = "3", IsCorrect = true },
                new Choice { Text = "4" },
                new Choice { Text = "9" },
            },
        };

        yield return new TrueFalseQuestion
        {
            Prompt = "The Pacific is the largest ocean.",
            Points = 1,
            CorrectAnswer = true,
        };

        var shortAnswer = new ShortAnswerQuestion
        {
            Prompt = "Name the gas that plants absorb for photosynthesis.",
            Points = 1,
        };
        shortAnswer.AcceptedAnswers.Add("carbon dioxide");
        shortAnswer.AcceptedAnswers.Add("CO2");
        yield return shortAnswer;

        var numeric = new NumericQuestion
        {
            Prompt = "What is 10 divided by 4?",
            Points = 1,
            Target = 2.5,
            Tolerance = 0.1,
            Unit = "units",
        };
        yield return numeric;

        var dropdown = new DropdownQuestion
        {
            Prompt = "Select the chemical symbol for gold.",
            Points = 1,
            Choices =
            {
                new Choice { Text = "Au", IsCorrect = true },
                new Choice { Text = "Ag" },
                new Choice { Text = "Gd" },
            },
        };
        yield return dropdown;

        var matching = new MatchingQuestion
        {
            Prompt = "Match each country to its capital.",
            Points = 2,
        };
        matching.Pairs.Add(new MatchPair { Left = "Japan", Right = "Tokyo" });
        matching.Pairs.Add(new MatchPair { Left = "Egypt", Right = "Cairo" });
        matching.Distractors.Add("Madrid");
        yield return matching;

        var sequence = new SequenceQuestion
        {
            Prompt = "Put these events in chronological order (earliest first).",
            Points = 2,
        };
        sequence.Items.Add("Sunrise");
        sequence.Items.Add("Noon");
        sequence.Items.Add("Sunset");
        yield return sequence;

        var fill = new FillInTheBlankQuestion
        {
            Prompt = "Water is made of {{1}} and {{2}}.",
            Points = 2,
        };
        fill.Blanks.Add(MakeBlank(1, "hydrogen"));
        fill.Blanks.Add(MakeBlank(2, "oxygen"));
        yield return fill;

        var essay = new EssayQuestion
        {
            Prompt = "Explain, in a short paragraph, why the sky appears blue.",
            Points = 5,
            RubricNotes = "Mentions scattering of shorter wavelengths.",
        };
        yield return essay;
    }

    private static Blank MakeBlank(int ordinal, string answer)
    {
        var blank = new Blank { Ordinal = ordinal };
        blank.AcceptedAnswers.Add(answer);
        return blank;
    }
}
