using System.Text;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// The import-template feature and the CSV layer that makes a CSV template
/// actually importable.
///
/// Two things are proved here. First, the CSV grid codec round-trips
/// adversarial content byte-for-byte -- the whole reason the schema uses one
/// column per field is undone if quoting is wrong, so this is the real risk and
/// was modelled in tools/port/csv_import_template_port.py first. Second, a
/// template (xlsx AND csv) imports cleanly back through the real importer, for
/// every question type, including a brand-new empty quiz.
/// </summary>
public class ImportTemplateTests
{
    private static ImportTemplateService Service() => new(new ExcelExporter());
    private static ExcelImporter Importer() => new();

    private static QuizDocument DocWith(params Question[] questions)
    {
        var doc = new QuizDocument { Title = "My Quiz" };
        var section = new Section { Title = "Chapter 1" };
        foreach (var q in questions) section.Questions.Add(q);
        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);
        return doc;
    }

    private static ImportResult ImportCsv(string csvText)
    {
        var bytes = Encoding.UTF8.GetBytes(csvText);
        using var stream = new MemoryStream(bytes);
        return Importer().Read(stream);
    }

    private static ImportResult ImportBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return Importer().Read(stream);
    }

    // ----- CsvGrid: RFC-4180 quoting --------------------------------------- //

    [Fact]
    public void CsvWriteQuotesOnlyFieldsThatNeedIt()
    {
        var text = CsvGrid.Write(new[]
        {
            new[] { "a", "b,c", "he said \"hi\"", "plain" },
        });

        Assert.Equal("a,\"b,c\",\"he said \"\"hi\"\"\",plain\r\n", text);
    }

    [Fact]
    public void CsvRoundTripsAdversarialGrid()
    {
        var original = new List<List<string>>
        {
            new() { "Section", "Type", "Prompt" },
            new() { "S1", "ShortAnswer", "and/or, \"quoted\", and\na newline" },
            new() { "", "", "" },
            new() { "x,y", "z\"z", "plain" },
        };

        var roundTripped = CsvGrid.Read(CsvGrid.Write(original));

        Assert.Equal(original.Count, roundTripped.Count);
        for (var r = 0; r < original.Count; r++)
            Assert.Equal(original[r], roundTripped[r]);
    }

    [Fact]
    public void CsvReadHandlesLastRowWithoutTrailingNewline()
    {
        var grid = CsvGrid.Read("a,b,c");
        Assert.Single(grid);
        Assert.Equal(new[] { "a", "b", "c" }, grid[0]);
    }

    [Fact]
    public void CsvReadKeepsEmptyMiddleField()
    {
        var grid = CsvGrid.Read("a,,c\r\n");
        Assert.Equal(new[] { "a", "", "c" }, grid[0]);
    }

    // ----- The importer accepts a CSV ------------------------------------- //

    [Fact]
    public void ImporterReadsAPlainCsv()
    {
        var csv =
            "Section,Type,Prompt,Points,Option 1,Correct 1\r\n"
            + "Math,MultipleChoiceSingle,2+2?,1,4,TRUE\r\n";

        var result = ImportCsv(csv);

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, result.QuestionCount);
        var q = Assert.IsType<MultipleChoiceSingleQuestion>(result.Document!.Sections[0].Questions[0]);
        Assert.Equal("2+2?", q.Prompt);
        Assert.Contains(q.Choices, c => c.Text == "4" && c.IsCorrect);
    }

    [Fact]
    public void CsvColumnsMayBeReorderedAndExtraColumnsIgnored()
    {
        // Prompt before Type, an unknown "Notes" column in the middle.
        var csv =
            "Prompt,Type,Notes,Section,Correct 1\r\n"
            + "The sky is blue.,TrueFalse,ignore me,Facts,TRUE\r\n";

        var result = ImportCsv(csv);

        Assert.True(result.Success, result.Error);
        var q = Assert.IsType<TrueFalseQuestion>(result.Document!.Sections[0].Questions[0]);
        Assert.True(q.CorrectAnswer);
        Assert.Equal("Facts", result.Document.Sections[0].Title);
    }

    [Fact]
    public void CsvQuotedFieldWithCommaSurvivesImport()
    {
        // A short answer that itself contains a comma: the per-column design plus
        // RFC-4180 quoting must keep it as ONE answer, not two.
        var csv =
            "Type,Prompt,Option 1\r\n"
            + "ShortAnswer,\"Name a mark, any one\",\"semicolon, maybe\"\r\n";

        var result = ImportCsv(csv);

        Assert.True(result.Success, result.Error);
        var q = Assert.IsType<ShortAnswerQuestion>(result.Document!.Sections[0].Questions[0]);
        Assert.Equal("Name a mark, any one", q.Prompt);
        Assert.Single(q.AcceptedAnswers);
        Assert.Equal("semicolon, maybe", q.AcceptedAnswers[0]);
    }

    [Fact]
    public void CsvWithoutTypeOrPromptColumnIsRejectedClearly()
    {
        var result = ImportCsv("Section,Points\r\nX,1\r\n");
        Assert.False(result.Success);
        Assert.Contains("Type", result.Error);
    }

    // ----- Template shape -------------------------------------------------- //

    [Fact]
    public void TemplateHasExampleSectionThenTheCurrentQuiz()
    {
        var quiz = DocWith(new ShortAnswerQuestion { Prompt = "User question" });
        var template = Service().BuildTemplateDocument(quiz);

        var sections = template.SectionsInDisplayOrder().ToList();
        Assert.Equal(ImportTemplateService.ExampleSectionTitle, sections[0].Title);
        Assert.True(sections[0].Questions.Count >= 10, "one example per common type");
        Assert.Contains(sections, s => s.Title == "Chapter 1");
    }

    [Fact]
    public void TemplateForAnEmptyQuizIsStillUsable()
    {
        // The whole point: help someone who has not authored anything yet.
        var template = Service().BuildTemplateDocument(new QuizDocument());
        var sections = template.SectionsInDisplayOrder().ToList();

        Assert.Single(sections);                               // just the examples
        Assert.Equal(ImportTemplateService.ExampleSectionTitle, sections[0].Title);
        Assert.NotEmpty(sections[0].Questions);
    }

    [Fact]
    public void TemplateDoesNotMutateTheLoadedQuiz()
    {
        var original = new ShortAnswerQuestion { Prompt = "Keep me" };
        var quiz = DocWith(original);

        var template = Service().BuildTemplateDocument(quiz);

        // The quiz still has exactly its one section and question, untouched.
        Assert.Single(quiz.Sections);
        Assert.Single(quiz.Sections[0].Questions);
        // And the template's copy is a different instance.
        var copied = template.Sections.Last().Questions[0];
        Assert.NotSame(original, copied);
    }

    // ----- Template round-trips through the importer ----------------------- //

    [Fact]
    public void XlsxTemplateImportsCleanly()
    {
        using var buffer = new MemoryStream();
        Service().WriteXlsx(buffer, new QuizDocument());

        var result = ImportBytes(buffer.ToArray());

        Assert.True(result.Success, result.Error);
        Assert.Empty(result.Problems);                         // examples are all legal
        Assert.True(result.QuestionCount >= 10);
    }

    [Fact]
    public void CsvTemplateImportsCleanly()
    {
        using var buffer = new MemoryStream();
        Service().WriteCsv(buffer, new QuizDocument());

        var result = ImportBytes(buffer.ToArray());

        Assert.True(result.Success, result.Error);
        Assert.Empty(result.Problems);
        Assert.True(result.QuestionCount >= 10);
    }

    [Fact]
    public void CsvTemplateCarriesEveryExampleQuestionType()
    {
        using var buffer = new MemoryStream();
        Service().WriteCsv(buffer, new QuizDocument());

        var result = ImportBytes(buffer.ToArray());
        Assert.True(result.Success, result.Error);

        var kinds = result.Document!.Sections
            .SelectMany(s => s.Questions)
            .Select(q => q.Kind)
            .ToHashSet();

        // Every type the Guide documents should be present as a worked example.
        foreach (var kind in new[]
                 {
                     QuestionKind.MultipleChoiceSingle, QuestionKind.MultipleChoiceMultiple,
                     QuestionKind.TrueFalse, QuestionKind.ShortAnswer, QuestionKind.FillInTheBlank,
                     QuestionKind.Matching, QuestionKind.Sequence, QuestionKind.Numeric,
                     QuestionKind.Dropdown, QuestionKind.Essay,
                 })
        {
            Assert.Contains(kind, kinds);
        }
    }

    [Fact]
    public void CsvTemplateCarriesTheLoadedQuizWithAwkwardContent()
    {
        // A prompt with a comma and a quote must survive the CSV round-trip as
        // part of the "filled example" portion of the template.
        var quiz = DocWith(new ShortAnswerQuestion
        {
            Prompt = "Define \"osmosis\", briefly.",
            AcceptedAnswers = { "water movement" },
        });

        using var buffer = new MemoryStream();
        Service().WriteCsv(buffer, quiz);

        var result = ImportBytes(buffer.ToArray());
        Assert.True(result.Success, result.Error);

        var imported = result.Document!.Sections
            .SelectMany(s => s.Questions)
            .OfType<ShortAnswerQuestion>()
            .FirstOrDefault(q => q.Prompt == "Define \"osmosis\", briefly.");

        Assert.NotNull(imported);
        Assert.Contains("water movement", imported!.AcceptedAnswers);
    }

    [Fact]
    public void XlsxTemplateAndCsvTemplateYieldTheSameQuestionCount()
    {
        var quiz = DocWith(new TrueFalseQuestion { Prompt = "Q", CorrectAnswer = true });

        using var xlsx = new MemoryStream();
        using var csv = new MemoryStream();
        Service().WriteXlsx(xlsx, quiz);
        Service().WriteCsv(csv, quiz);

        var fromXlsx = ImportBytes(xlsx.ToArray());
        var fromCsv = ImportBytes(csv.ToArray());

        Assert.True(fromXlsx.Success && fromCsv.Success);
        Assert.Equal(fromXlsx.QuestionCount, fromCsv.QuestionCount);
    }
}
