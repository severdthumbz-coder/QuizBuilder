using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// The round-trip tests are the real ones: export, import, compare. Bulk editing
/// in Excel is the whole point of the feature, so anything the exporter can emit
/// that the importer cannot read is a defect.
///
/// The importer tests build workbooks the way EXCEL writes them -- shared
/// strings, omitted cells, rich text runs -- rather than the way this app writes
/// them. An importer tested only against its own exporter passes happily and
/// then fails on the first real file.
/// </summary>
public class ExcelExporterTests
{
    private static readonly XNamespace S =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // --- Helpers ------------------------------------------------------------

    private static byte[] Export(QuizDocument document)
    {
        using var stream = new MemoryStream();
        new ExcelExporter().Write(stream, document);
        return stream.ToArray();
    }

    private static ImportResult Import(byte[] xlsx)
    {
        using var stream = new MemoryStream(xlsx);
        return new ExcelImporter().Read(stream);
    }

    private static ImportResult RoundTrip(QuizDocument document) => Import(Export(document));

    private static XDocument PartOf(byte[] xlsx, string path)
    {
        using var archive = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
        var entry = archive.GetEntry(path);

        Assert.NotNull(entry);

        using var stream = entry!.Open();
        return XDocument.Load(stream);
    }

    private static QuizDocument DocWith(params Question[] questions)
    {
        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "Part A" };

        foreach (var q in questions) section.Questions.Add(q);

        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);

        return doc;
    }

    private static Question First(ImportResult result)
    {
        Assert.True(result.Success, result.Error);
        return result.Document!.Sections[0].Questions[0];
    }

    // --- Numeric + Dropdown round-trips (v3 types) --------------------------

    [Fact]
    public void NumericRoundTripsTargetToleranceAndUnit()
    {
        var result = RoundTrip(DocWith(new NumericQuestion
        {
            Prompt = "Speed of light (approx, ×10^8 m/s)?",
            Points = 2,
            Target = 3.0,
            Tolerance = 0.1,
            Unit = "×10^8 m/s",
        }));

        var q = Assert.IsType<NumericQuestion>(First(result));
        Assert.Equal(3.0, q.Target);
        Assert.Equal(0.1, q.Tolerance);
        Assert.Equal("×10^8 m/s", q.Unit);
        Assert.Equal(2, q.Points);
    }

    [Fact]
    public void NumericRoundTripsWithoutToleranceOrUnit()
    {
        var result = RoundTrip(DocWith(new NumericQuestion
        {
            Prompt = "2 + 2?",
            Target = 4.0,
        }));

        var q = Assert.IsType<NumericQuestion>(First(result));
        Assert.Equal(4.0, q.Target);
        Assert.Equal(0, q.Tolerance);
        Assert.True(string.IsNullOrEmpty(q.Unit));
    }

    [Fact]
    public void DropdownRoundTripsChoicesAndCorrect()
    {
        var dropdown = new DropdownQuestion { Prompt = "Which year?", Points = 1 };
        dropdown.Choices.Add(new Choice { Text = "1990" });
        dropdown.Choices.Add(new Choice { Text = "2000", IsCorrect = true });
        dropdown.Choices.Add(new Choice { Text = "2010" });

        var q = Assert.IsType<DropdownQuestion>(First(RoundTrip(DocWith(dropdown))));

        Assert.Equal(3, q.Choices.Count);
        Assert.Equal("2000", q.Choices.Single(c => c.IsCorrect).Text);
        Assert.Equal(new[] { "1990", "2000", "2010" }, q.Choices.Select(c => c.Text).ToArray());
    }

    // --- Package ------------------------------------------------------------

    [Fact]
    public void ProducesAValidZipWithEveryRequiredPart()
    {
        var xlsx = Export(DocWith(new EssayQuestion { Prompt = "Q", Points = 1 }));

        using var archive = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
        var names = archive.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("[Content_Types].xml", names);
        Assert.Contains("_rels/.rels", names);
        Assert.Contains("xl/workbook.xml", names);
        Assert.Contains("xl/_rels/workbook.xml.rels", names);
        Assert.Contains("xl/worksheets/sheet1.xml", names);
        Assert.Contains("xl/worksheets/sheet2.xml", names);
        Assert.Contains("xl/styles.xml", names);
    }

    [Fact]
    public void EveryPartIsWellFormedXml()
    {
        var xlsx = Export(DocWith(new EssayQuestion { Prompt = "Q", Points = 1 }));

        using var archive = new ZipArchive(new MemoryStream(xlsx), ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            using var stream = entry.Open();
            XDocument.Load(stream);
        }
    }

    [Fact]
    public void TheCallersStreamIsLeftOpen()
    {
        using var stream = new MemoryStream();
        new ExcelExporter().Write(stream, DocWith(new EssayQuestion { Prompt = "Q" }));

        Assert.True(stream.CanRead);
        Assert.True(stream.Length > 0);
    }

    [Fact]
    public void ThereIsAGuideSheet()
    {
        var xlsx = Export(DocWith(new EssayQuestion { Prompt = "Q" }));
        var workbook = PartOf(xlsx, "xl/workbook.xml");

        var names = workbook.Descendants(S + "sheet")
            .Select(s => (string?)s.Attribute("name")).ToList();

        // The person editing the spreadsheet is looking at the spreadsheet, not
        // at a README.
        Assert.Contains(QuizSheetSchema.QuestionsSheetName, names);
        Assert.Contains(QuizSheetSchema.GuideSheetName, names);
    }

    [Fact]
    public void EmptyCellsAreOmittedEntirely()
    {
        // Matching Excel's own behaviour. Worth pinning: it is exactly what
        // makes positional reading wrong, so if this ever changes the importer
        // tests must still cover the gap case.
        var xlsx = Export(DocWith(new EssayQuestion { Prompt = "Q", Points = 1 }));
        var sheet = PartOf(xlsx, "xl/worksheets/sheet1.xml");

        var dataRow = sheet.Descendants(S + "row")
            .First(r => (string?)r.Attribute("r") == "2");

        // Section, Type, Prompt, Points -- and no Hint, no options.
        Assert.True(dataRow.Elements(S + "c").Count() < QuizSheetSchema.Headers.Count);
    }

    // --- Round-trip, every type ---------------------------------------------

    [Fact]
    public void MultipleChoiceSingleRoundTrips()
    {
        var q = new MultipleChoiceSingleQuestion { Prompt = "Pick one", Points = 2, Hint = "a hint" };
        q.Choices.Add(new Choice { Text = "right", IsCorrect = true });
        q.Choices.Add(new Choice { Text = "wrong" });

        var imported = Assert.IsType<MultipleChoiceSingleQuestion>(First(RoundTrip(DocWith(q))));

        Assert.Equal("Pick one", imported.Prompt);
        Assert.Equal(2, imported.Points);
        Assert.Equal("a hint", imported.Hint);
        Assert.Equal(2, imported.Choices.Count);
        Assert.Equal("right", imported.Choices[0].Text);
        Assert.True(imported.Choices[0].IsCorrect);
        Assert.False(imported.Choices[1].IsCorrect);
    }

    [Fact]
    public void MultipleChoiceMultipleRoundTrips()
    {
        var q = new MultipleChoiceMultipleQuestion { Prompt = "Pick many", Points = 3 };
        q.Choices.Add(new Choice { Text = "a", IsCorrect = true });
        q.Choices.Add(new Choice { Text = "b", IsCorrect = true });
        q.Choices.Add(new Choice { Text = "c" });

        var imported = Assert.IsType<MultipleChoiceMultipleQuestion>(First(RoundTrip(DocWith(q))));

        Assert.Equal(3, imported.Choices.Count);
        Assert.Equal(2, imported.Choices.Count(c => c.IsCorrect));
    }

    [Fact]
    public void TrueFalseRoundTrips()
    {
        var imported = Assert.IsType<TrueFalseQuestion>(
            First(RoundTrip(DocWith(new TrueFalseQuestion { Prompt = "Is it?", CorrectAnswer = false }))));

        Assert.False(imported.CorrectAnswer);
    }

    [Fact]
    public void ShortAnswerRoundTrips()
    {
        var q = new ShortAnswerQuestion { Prompt = "Name it", Points = 2 };
        q.AcceptedAnswers.Add("Paris");
        q.AcceptedAnswers.Add("paris");

        var imported = Assert.IsType<ShortAnswerQuestion>(First(RoundTrip(DocWith(q))));

        Assert.Equal(new[] { "Paris", "paris" }, imported.AcceptedAnswers);
    }

    [Fact]
    public void FillInTheBlankRoundTrips()
    {
        var q = new FillInTheBlankQuestion { Prompt = "The {{1}} is {{2}}", Points = 2 };
        q.Blanks.Add(new Blank { Ordinal = 1, AcceptedAnswers = { "cat" } });
        q.Blanks.Add(new Blank { Ordinal = 2, AcceptedAnswers = { "black" } });

        var imported = Assert.IsType<FillInTheBlankQuestion>(First(RoundTrip(DocWith(q))));

        Assert.Equal(2, imported.Blanks.Count);
        Assert.Equal("cat", imported.Blanks[0].AcceptedAnswers[0]);
        Assert.Equal(2, imported.Blanks[1].Ordinal);
        Assert.Equal("black", imported.Blanks[1].AcceptedAnswers[0]);
    }

    [Fact]
    public void MatchingRoundTrips()
    {
        var q = new MatchingQuestion { Prompt = "Match", Points = 3 };
        q.Pairs.Add(new MatchPair { Left = "One", Right = "Uno" });
        q.Pairs.Add(new MatchPair { Left = "Two", Right = "Dos" });
        q.Distractors.Add("Tres");

        var imported = Assert.IsType<MatchingQuestion>(First(RoundTrip(DocWith(q))));

        Assert.Equal(2, imported.Pairs.Count);
        Assert.Equal("One", imported.Pairs[0].Left);
        Assert.Equal("Uno", imported.Pairs[0].Right);
        Assert.Equal(new[] { "Tres" }, imported.Distractors);
    }

    [Fact]
    public void EssayRoundTrips()
    {
        var imported = Assert.IsType<EssayQuestion>(
            First(RoundTrip(DocWith(new EssayQuestion
            {
                Prompt = "Discuss",
                Points = 10,
                RubricNotes = "Look for structure",
            }))));

        Assert.Equal(10, imported.Points);
        Assert.Equal("Look for structure", imported.RubricNotes);
    }

    // --- Round-trip: content that would break a delimiter -------------------

    [Fact]
    public void ContentContainingDelimitersSurvives()
    {
        // The first schema packed pairs into "Left = Right" and answers into
        // "a / b". Both collide with real content: "and/or" is a legitimate
        // short answer, and it silently became two. Separate columns cannot
        // collide with anything, which is the whole reason they exist.
        var matching = new MatchingQuestion { Prompt = "M", Points = 1 };
        matching.Pairs.Add(new MatchPair { Left = "x = y", Right = "a = b" });
        matching.Distractors.Add("p, q");

        var shortAnswer = new ShortAnswerQuestion { Prompt = "S", Points = 1 };
        shortAnswer.AcceptedAnswers.Add("and/or");
        shortAnswer.AcceptedAnswers.Add("either, or");

        var result = RoundTrip(DocWith(matching, shortAnswer));
        Assert.True(result.Success, result.Error);

        var m = Assert.IsType<MatchingQuestion>(result.Document!.Sections[0].Questions[0]);
        Assert.Equal("x = y", m.Pairs[0].Left);
        Assert.Equal("a = b", m.Pairs[0].Right);
        Assert.Equal(new[] { "p, q" }, m.Distractors);

        var s = Assert.IsType<ShortAnswerQuestion>(result.Document.Sections[0].Questions[1]);
        Assert.Equal(new[] { "and/or", "either, or" }, s.AcceptedAnswers);
    }

    [Fact]
    public void HostileTextSurvives()
    {
        var q = new EssayQuestion { Prompt = "If x < 5 & y > 3, \"discuss\"", Points = 1 };

        var imported = First(RoundTrip(DocWith(q)));

        Assert.Equal("If x < 5 & y > 3, \"discuss\"", imported.Prompt);
    }

    [Fact]
    public void AControlCharacterDoesNotCorruptTheWorkbook()
    {
        var q = new EssayQuestion { Prompt = "before\u0001after", Points = 1 };

        var imported = First(RoundTrip(DocWith(q)));

        Assert.Equal("beforeafter", imported.Prompt);
        Assert.DoesNotContain(imported.Prompt, c => c == '\u0001');
    }

    [Fact]
    public void FractionalPointsSurvive()
    {
        var imported = First(RoundTrip(DocWith(new EssayQuestion { Prompt = "Q", Points = 2.5 })));

        // The file format stores 2.5 invariantly whatever the machine's locale;
        // parsing with the current culture would read it as 25 on a de-DE box.
        Assert.Equal(2.5, imported.Points);
    }

    [Fact]
    public void SectionsAreRebuiltInDisplayOrder()
    {
        var doc = new QuizDocument { Title = "T" };
        foreach (var title in new[] { "First", "Second" })
        {
            var section = new Section { Title = title };
            section.Questions.Add(new EssayQuestion { Prompt = $"{title} Q", Points = 1 });
            doc.Sections.Add(section);
            doc.SectionDisplayOrder.Add(section.Id);
        }

        var result = RoundTrip(doc);
        Assert.True(result.Success, result.Error);

        Assert.Equal(2, result.Document!.Sections.Count);
        Assert.Equal("First", result.Document.Sections[0].Title);
        Assert.Equal("Second", result.Document.Sections[1].Title);
    }

    [Fact]
    public void RowsWithTheSameSectionNameAreGrouped()
    {
        var doc = new QuizDocument { Title = "T" };
        var a = new Section { Title = "Part A" };
        a.Questions.Add(new EssayQuestion { Prompt = "Q1", Points = 1 });
        a.Questions.Add(new EssayQuestion { Prompt = "Q2", Points = 1 });
        doc.Sections.Add(a);
        doc.SectionDisplayOrder.Add(a.Id);

        var result = RoundTrip(doc);

        Assert.Single(result.Document!.Sections);
        Assert.Equal(2, result.Document.Sections[0].Questions.Count);
    }

    [Fact]
    public void ImportedQuestionsGetFreshIds()
    {
        var q = new EssayQuestion { Prompt = "Q", Points = 1 };
        var originalId = q.Id;

        var imported = First(RoundTrip(DocWith(q)));

        // The sheet carries no id, so a collision with an existing question
        // would be pure coincidence -- but pinning it documents that import
        // creates new questions rather than updating in place.
        Assert.NotEqual(originalId, imported.Id);
    }
}

/// <summary>
/// Importer tests using workbooks shaped the way Excel writes them.
/// </summary>
public class ExcelImporterTests
{
    private const string Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    /// <summary>
    /// Builds a workbook the way Excel does: a sharedStrings table, cells
    /// referencing it by index, and a worksheet part whose name does not match
    /// its position.
    /// </summary>
    private static byte[] ExcelStyleWorkbook(
        IReadOnlyList<string> sharedStrings,
        string rowsXml,
        string sheetPartName = "worksheets/sheet7.xml")
    {
        var sst = new StringBuilder();
        sst.Append($"""<?xml version="1.0"?><sst xmlns="{Ns}" count="{sharedStrings.Count}" uniqueCount="{sharedStrings.Count}">""");
        foreach (var s in sharedStrings)
            sst.Append($"""<si><t xml:space="preserve">{System.Security.SecurityElement.Escape(s)}</t></si>""");
        sst.Append("</sst>");

        var sheet = $"""<?xml version="1.0"?><worksheet xmlns="{Ns}"><sheetData>{rowsXml}</sheetData></worksheet>""";

        var workbook = $"""
            <?xml version="1.0"?>
            <workbook xmlns="{Ns}" xmlns:r="{Rel}">
              <sheets><sheet name="Questions" sheetId="1" r:id="rId1"/></sheets>
            </workbook>
            """;

        var workbookRels = $"""
            <?xml version="1.0"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="{Rel}/worksheet" Target="{sheetPartName}"/>
              <Relationship Id="rId2" Type="{Rel}/sharedStrings" Target="sharedStrings.xml"/>
            </Relationships>
            """;

        var rels = $"""
            <?xml version="1.0"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="{Rel}/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """;

        var contentTypes = """
            <?xml version="1.0"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
            </Types>
            """;

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                using var stream = archive.CreateEntry(path).Open();
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(content);
            }

            Add("[Content_Types].xml", contentTypes);
            Add("_rels/.rels", rels);
            Add("xl/workbook.xml", workbook);
            Add("xl/_rels/workbook.xml.rels", workbookRels);
            Add("xl/sharedStrings.xml", sst.ToString());
            Add("xl/" + sheetPartName, sheet);
        }

        return buffer.ToArray();
    }

    private static ImportResult Read(byte[] xlsx)
    {
        using var stream = new MemoryStream(xlsx);
        return new ExcelImporter().Read(stream);
    }

    [Fact]
    public void ReadsSharedStrings()
    {
        var shared = new[] { "Type", "Prompt", "Essay", "Discuss this" };

        var rows = """
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
            <row r="2"><c r="A2" t="s"><v>2</v></c><c r="B2" t="s"><v>3</v></c></row>
            """;

        var result = Read(ExcelStyleWorkbook(shared, rows));

        Assert.True(result.Success, result.Error);
        Assert.Equal("Discuss this", result.Document!.Sections[0].Questions[0].Prompt);
    }

    [Fact]
    public void AnOmittedCellDoesNotShiftEveryFieldAfterIt()
    {
        // THE bug this importer exists to avoid. Excel writes no element at all
        // for an empty cell, so a row with A, B, D filled has three elements --
        // and reading them in order puts D's value into C's field, shifting
        // everything after. The result is plausible and wrong.
        var shared = new[] { "Type", "Prompt", "Hint", "Points", "Essay", "Discuss", "9" };

        // C (Hint) is omitted entirely.
        var rows = """
            <row r="1">
              <c r="A1" t="s"><v>0</v></c>
              <c r="B1" t="s"><v>1</v></c>
              <c r="C1" t="s"><v>2</v></c>
              <c r="D1" t="s"><v>3</v></c>
            </row>
            <row r="2">
              <c r="A2" t="s"><v>4</v></c>
              <c r="B2" t="s"><v>5</v></c>
              <c r="D2"><v>9</v></c>
            </row>
            """;

        var result = Read(ExcelStyleWorkbook(shared, rows));

        Assert.True(result.Success, result.Error);

        var question = result.Document!.Sections[0].Questions[0];

        Assert.Equal("Discuss", question.Prompt);
        Assert.Null(question.Hint);      // not "9"
        Assert.Equal(9, question.Points); // not shifted into Hint
    }

    [Fact]
    public void ReadsRichTextSplitAcrossRuns()
    {
        // Excel splits styled text into runs. Reading only the first <t> turns
        // "Hello world" with one bold word into "Hello ".
        var sst = $"""
            <?xml version="1.0"?><sst xmlns="{Ns}" count="3" uniqueCount="3">
              <si><t>Type</t></si>
              <si><t>Prompt</t></si>
              <si><r><t xml:space="preserve">Pick the </t></r><r><t>right one</t></r></si>
            </sst>
            """;

        var sheet = $"""
            <?xml version="1.0"?><worksheet xmlns="{Ns}"><sheetData>
              <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
              <row r="2"><c r="A2" t="inlineStr"><is><t>Essay</t></is></c><c r="B2" t="s"><v>2</v></c></row>
            </sheetData></worksheet>
            """;

        var workbook = $"""<?xml version="1.0"?><workbook xmlns="{Ns}" xmlns:r="{Rel}"><sheets><sheet name="Questions" sheetId="1" r:id="rId1"/></sheets></workbook>""";
        var workbookRels = $"""<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="{Rel}/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""";
        var rels = $"""<?xml version="1.0"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="{Rel}/officeDocument" Target="xl/workbook.xml"/></Relationships>""";

        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                using var stream = archive.CreateEntry(path).Open();
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(content);
            }

            Add("[Content_Types].xml", """<?xml version="1.0"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/></Types>""");
            Add("_rels/.rels", rels);
            Add("xl/workbook.xml", workbook);
            Add("xl/_rels/workbook.xml.rels", workbookRels);
            Add("xl/sharedStrings.xml", sst);
            Add("xl/worksheets/sheet1.xml", sheet);
        }

        var result = Read(buffer.ToArray());

        Assert.True(result.Success, result.Error);
        Assert.Equal("Pick the right one", result.Document!.Sections[0].Questions[0].Prompt);
    }

    [Fact]
    public void FindsTheSheetThroughItsRelationshipNotItsFileName()
    {
        // Excel does not renumber parts when sheets are added or deleted, so the
        // first sheet in a real workbook is routinely sheet3.xml.
        var shared = new[] { "Type", "Prompt", "Essay", "Discuss" };

        var rows = """
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
            <row r="2"><c r="A2" t="s"><v>2</v></c><c r="B2" t="s"><v>3</v></c></row>
            """;

        var result = Read(ExcelStyleWorkbook(shared, rows, sheetPartName: "worksheets/sheet7.xml"));

        Assert.True(result.Success, result.Error);
        Assert.Single(result.Document!.Sections[0].Questions);
    }

    [Fact]
    public void ColumnOrderDoesNotMatter()
    {
        var shared = new[] { "Prompt", "Type", "Discuss", "Essay" };

        // Prompt first, Type second -- the reverse of the export layout.
        var rows = """
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
            <row r="2"><c r="A2" t="s"><v>2</v></c><c r="B2" t="s"><v>3</v></c></row>
            """;

        var result = Read(ExcelStyleWorkbook(shared, rows));

        Assert.True(result.Success, result.Error);
        Assert.Equal("Discuss", result.Document!.Sections[0].Questions[0].Prompt);
    }

    [Fact]
    public void UnknownColumnsAreIgnored()
    {
        var shared = new[] { "Type", "Prompt", "My own notes", "Essay", "Discuss", "ignore me" };

        var rows = """
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="s"><v>2</v></c></row>
            <row r="2"><c r="A2" t="s"><v>3</v></c><c r="B2" t="s"><v>4</v></c><c r="C2" t="s"><v>5</v></c></row>
            """;

        var result = Read(ExcelStyleWorkbook(shared, rows));

        Assert.True(result.Success, result.Error);
        Assert.Single(result.Document!.Sections[0].Questions);
    }

    [Fact]
    public void HeaderNamesAreMatchedLoosely()
    {
        var shared = new[] { "  TYPE  ", "prompt", "Essay", "Discuss" };

        var rows = """
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
            <row r="2"><c r="A2" t="s"><v>2</v></c><c r="B2" t="s"><v>3</v></c></row>
            """;

        var result = Read(ExcelStyleWorkbook(shared, rows));

        Assert.True(result.Success, result.Error);
    }

    [Fact]
    public void TypeNamesAreMatchedLoosely()
    {
        var shared = new[] { "Type", "Prompt", "multiple choice single", "Pick", "Option 1", "Correct 1", "right", "TRUE" };

        var rows = """
            <row r="1">
              <c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c>
              <c r="C1" t="s"><v>4</v></c><c r="D1" t="s"><v>5</v></c>
            </row>
            <row r="2">
              <c r="A2" t="s"><v>2</v></c><c r="B2" t="s"><v>3</v></c>
              <c r="C2" t="s"><v>6</v></c><c r="D2" t="s"><v>7</v></c>
            </row>
            """;

        var result = Read(ExcelStyleWorkbook(shared, rows));

        Assert.True(result.Success, result.Error);
        Assert.IsType<MultipleChoiceSingleQuestion>(result.Document!.Sections[0].Questions[0]);
    }

    [Fact]
    public void BlankRowsAreSkipped()
    {
        var shared = new[] { "Type", "Prompt", "Essay", "Discuss" };

        var rows = """
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
            <row r="2"><c r="A2" t="s"><v>2</v></c><c r="B2" t="s"><v>3</v></c></row>
            <row r="3"/>
            <row r="4"><c r="A4" t="s"><v>2</v></c><c r="B4" t="s"><v>3</v></c></row>
            """;

        var result = Read(ExcelStyleWorkbook(shared, rows));

        Assert.True(result.Success, result.Error);
        Assert.Equal(2, result.QuestionCount);
    }

    // --- Reporting problems -------------------------------------------------

    [Fact]
    public void ARowWithNoTypeIsReportedNotSilentlyDropped()
    {
        var shared = new[] { "Type", "Prompt", "Essay", "Good", "Orphan" };

        var rows = """
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
            <row r="2"><c r="A2" t="s"><v>2</v></c><c r="B2" t="s"><v>3</v></c></row>
            <row r="3"><c r="B3" t="s"><v>4</v></c></row>
            """;

        var result = Read(ExcelStyleWorkbook(shared, rows));

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, result.QuestionCount);

        // A partial import that claims success is worse than an error: the
        // teacher prints a paper quietly missing a question.
        Assert.Contains(result.Problems, p => p.Contains("Row 3"));
    }

    [Fact]
    public void AnUnknownTypeIsReported()
    {
        var shared = new[] { "Type", "Prompt", "Interpretive Dance", "Perform" };

        var rows = """
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
            <row r="2"><c r="A2" t="s"><v>2</v></c><c r="B2" t="s"><v>3</v></c></row>
            """;

        var result = Read(ExcelStyleWorkbook(shared, rows));

        Assert.False(result.Success);
        Assert.Contains(result.Problems, p => p.Contains("Interpretive Dance"));
    }

    [Fact]
    public void ErrorsCiteTheSpreadsheetRowNumber()
    {
        // The r= attribute, not a loop counter: a deleted row leaves a gap, and
        // an error citing the wrong row sends the user to the wrong place.
        var shared = new[] { "Type", "Prompt", "Essay", "Good", "Orphan" };

        var rows = """
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
            <row r="2"><c r="A2" t="s"><v>2</v></c><c r="B2" t="s"><v>3</v></c></row>
            <row r="9"><c r="B9" t="s"><v>4</v></c></row>
            """;

        var result = Read(ExcelStyleWorkbook(shared, rows));

        Assert.Contains(result.Problems, p => p.Contains("Row 9"));
    }

    [Fact]
    public void UnreadablePointsAreReportedAndDefaulted()
    {
        var shared = new[] { "Type", "Prompt", "Points", "Essay", "Discuss", "two" };

        var rows = """
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="s"><v>2</v></c></row>
            <row r="2"><c r="A2" t="s"><v>3</v></c><c r="B2" t="s"><v>4</v></c><c r="C2" t="s"><v>5</v></c></row>
            """;

        var result = Read(ExcelStyleWorkbook(shared, rows));

        Assert.True(result.Success, result.Error);
        Assert.Equal(1, result.Document!.Sections[0].Questions[0].Points);
        Assert.Contains(result.Problems, p => p.Contains("Points"));
    }

    [Fact]
    public void TwoCorrectAnswersOnASingleChoiceQuestionKeepsTheFirstAndSaysSo()
    {
        var shared = new[] { "Type", "Prompt", "Option 1", "Option 2", "Correct 1", "Correct 2",
                             "MultipleChoiceSingle", "Pick", "a", "b", "TRUE", "TRUE" };

        var rows = """
            <row r="1">
              <c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c>
              <c r="C1" t="s"><v>2</v></c><c r="D1" t="s"><v>3</v></c>
              <c r="E1" t="s"><v>4</v></c><c r="F1" t="s"><v>5</v></c>
            </row>
            <row r="2">
              <c r="A2" t="s"><v>6</v></c><c r="B2" t="s"><v>7</v></c>
              <c r="C2" t="s"><v>8</v></c><c r="D2" t="s"><v>9</v></c>
              <c r="E2" t="s"><v>10</v></c><c r="F2" t="s"><v>11</v></c>
            </row>
            """;

        var result = Read(ExcelStyleWorkbook(shared, rows));

        Assert.True(result.Success, result.Error);

        var q = Assert.IsType<MultipleChoiceSingleQuestion>(result.Document!.Sections[0].Questions[0]);

        Assert.Single(q.Choices, c => c.IsCorrect);
        Assert.True(q.Choices[0].IsCorrect);
        Assert.Contains(result.Problems, p => p.Contains("marked TRUE"));
    }

    [Fact]
    public void ExcelBooleansAreUnderstood()
    {
        // Excel stores a real boolean as 1/0, not "TRUE".
        var shared = new[] { "Type", "Prompt", "Correct 1", "TrueFalse", "Is it?" };

        var rows = """
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c><c r="C1" t="s"><v>2</v></c></row>
            <row r="2"><c r="A2" t="s"><v>3</v></c><c r="B2" t="s"><v>4</v></c><c r="C2" t="b"><v>0</v></c></row>
            """;

        var result = Read(ExcelStyleWorkbook(shared, rows));

        Assert.True(result.Success, result.Error);

        var q = Assert.IsType<TrueFalseQuestion>(result.Document!.Sections[0].Questions[0]);
        Assert.False(q.CorrectAnswer);
    }

    // --- Bad files ----------------------------------------------------------

    [Fact]
    public void AFileThatIsNotAZipFailsWithAUsefulMessage()
    {
        var result = Read(Encoding.UTF8.GetBytes("this is not a spreadsheet"));

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains(".xlsx", result.Error!);
    }

    [Fact]
    public void ASheetWithNoTypeOrPromptColumnFails()
    {
        var shared = new[] { "Colour", "Size", "red", "large" };

        var rows = """
            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
            <row r="2"><c r="A2" t="s"><v>2</v></c><c r="B2" t="s"><v>3</v></c></row>
            """;

        var result = Read(ExcelStyleWorkbook(shared, rows));

        Assert.False(result.Success);
        Assert.Contains("Type", result.Error!);
    }

    [Fact]
    public void AHeaderOnlySheetFails()
    {
        var shared = new[] { "Type", "Prompt" };

        var rows = """<row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>""";

        var result = Read(ExcelStyleWorkbook(shared, rows));

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }
}
