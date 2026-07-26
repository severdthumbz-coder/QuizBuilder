using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Writes a quiz as an .xlsx for bulk editing, then reading back via
/// <see cref="ExcelImporter"/>.
///
/// No library, for the same reason as WordExporter: an .xlsx is a ZIP of XML,
/// System.IO.Compression is already in the BCL, and a NuGet dependency could not
/// be exercised in the environment this was written in. ClosedXML is MIT but
/// large; EPPlus has been non-commercial-only since v5, which is a licence trap
/// for an app someone might sell.
///
/// This writes the AUTHORED document, not a compiled paper: the point is to edit
/// questions, so shuffling or selecting a subset would be actively wrong.
/// </summary>
public sealed class ExcelExporter : IExcelExporter
{
    private const string SpreadsheetMlNamespace =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string RelationshipsNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public void Write(Stream stream, QuizDocument document)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(document);

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);

        WriteEntry(archive, "[Content_Types].xml", ContentTypesXml());
        WriteEntry(archive, "_rels/.rels", PackageRelsXml());
        WriteEntry(archive, "xl/workbook.xml", WorkbookXml());
        WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelsXml());
        WriteEntry(archive, "xl/styles.xml", StylesXml());
        WriteEntry(archive, "xl/worksheets/sheet1.xml", QuestionsSheetXml(document));
        WriteEntry(archive, "xl/worksheets/sheet2.xml", GuideSheetXml());
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);

        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, new UTF8Encoding(false));

        writer.Write(content);
    }

    // --- Package parts ------------------------------------------------------

    private static string ContentTypesXml() =>
        """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
          <Default Extension="xml" ContentType="application/xml"/>
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
          <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
        </Types>
        """;

    private static string PackageRelsXml() =>
        $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="{RelationshipsNamespace}/officeDocument" Target="xl/workbook.xml"/>
        </Relationships>
        """;

    private static string WorkbookXml() =>
        $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <workbook xmlns="{SpreadsheetMlNamespace}" xmlns:r="{RelationshipsNamespace}">
          <sheets>
            <sheet name="{QuizSheetSchema.QuestionsSheetName}" sheetId="1" r:id="rId1"/>
            <sheet name="{QuizSheetSchema.GuideSheetName}" sheetId="2" r:id="rId2"/>
          </sheets>
        </workbook>
        """;

    private static string WorkbookRelsXml() =>
        $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="{RelationshipsNamespace}/worksheet" Target="worksheets/sheet1.xml"/>
          <Relationship Id="rId2" Type="{RelationshipsNamespace}/worksheet" Target="worksheets/sheet2.xml"/>
          <Relationship Id="rId3" Type="{RelationshipsNamespace}/styles" Target="styles.xml"/>
        </Relationships>
        """;

    /// <summary>
    /// Two styles: a bold header row and a wrapping cell for prose. Excel needs
    /// the cellXfs indices to exist even if barely used -- a style index that
    /// points nowhere makes the file unopenable.
    /// </summary>
    private static string StylesXml() =>
        $"""
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="{SpreadsheetMlNamespace}">
          <fonts count="2">
            <font><sz val="11"/><name val="Calibri"/></font>
            <font><b/><sz val="11"/><name val="Calibri"/></font>
          </fonts>
          <fills count="2">
            <fill><patternFill patternType="none"/></fill>
            <fill><patternFill patternType="gray125"/></fill>
          </fills>
          <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
          <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
          <cellXfs count="3">
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
            <xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/>
            <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1">
              <alignment vertical="top" wrapText="1"/>
            </xf>
          </cellXfs>
        </styleSheet>
        """;

    // --- Questions sheet ----------------------------------------------------

    private static string QuestionsSheetXml(QuizDocument document)
    {
        var sb = new StringBuilder();

        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.AppendLine($"""<worksheet xmlns="{SpreadsheetMlNamespace}">""");

        AppendColumnWidths(sb);

        sb.AppendLine("<sheetData>");

        var headers = QuizSheetSchema.Headers;

        sb.Append("""<row r="1">""");
        for (var i = 0; i < headers.Count; i++)
            AppendInlineString(sb, CellRef(i, 1), headers[i], styleIndex: 1);
        sb.AppendLine("</row>");

        var rowNumber = 2;

        // SectionsInDisplayOrder, not Sections: the sheet must match what the
        // author sees and what the exported paper prints. Reading the backing
        // list directly is how the display order and the exported order drifted
        // apart once already.
        foreach (var section in document.SectionsInDisplayOrder())
        {
            foreach (var question in section.Questions)
            {
                AppendQuestionRow(sb, rowNumber, section.Title, question);
                rowNumber++;
            }
        }

        sb.AppendLine("</sheetData>");
        sb.AppendLine("</worksheet>");

        return sb.ToString();
    }

    private static void AppendColumnWidths(StringBuilder sb)
    {
        var headers = QuizSheetSchema.Headers;

        sb.AppendLine("<cols>");

        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];

            // Prompt and Extra hold prose; the rest hold short values. Excel's
            // default width makes a prompt column unreadable.
            var width = header == QuizSheetSchema.Prompt ? 60
                : header == QuizSheetSchema.Extra ? 40
                : header == QuizSheetSchema.Hint ? 30
                : header.StartsWith("Option", StringComparison.Ordinal)
                  || header.StartsWith("Match", StringComparison.Ordinal)
                  || header.StartsWith("Distractor", StringComparison.Ordinal) ? 22
                : 14;

            sb.AppendLine(
                $"""<col min="{i + 1}" max="{i + 1}" width="{width}" customWidth="1"/>""");
        }

        sb.AppendLine("</cols>");
    }

    private static void AppendQuestionRow(StringBuilder sb, int rowNumber, string sectionTitle, Question question)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [QuizSheetSchema.Section] = sectionTitle,
            [QuizSheetSchema.Type] = TypeName(question),
            [QuizSheetSchema.Prompt] = question.Prompt,
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
                // One answer per blank. The model allows alternatives, but a
                // second answer for blank 1 has nowhere to go in a rectangle
                // without a delimiter -- and a delimiter collides with answers
                // like "and/or". The Guide sheet says so rather than losing them
                // quietly.
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
                // Items are written in their correct (authored) order into the
                // Option columns. There is no separate "correct" marking: the
                // order itself is the answer, and the importer reads it back in
                // sheet order.
                for (var i = 0; i < Math.Min(q.Items.Count, QuizSheetSchema.MaxOptions); i++)
                    values[QuizSheetSchema.Option(i + 1)] = q.Items[i];
                break;

            case EssayQuestion q:
                values[QuizSheetSchema.Extra] = q.RubricNotes ?? string.Empty;
                break;
        }

        sb.Append($"""<row r="{rowNumber}">""");

        var headers = QuizSheetSchema.Headers;
        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            var reference = CellRef(i, rowNumber);

            if (header == QuizSheetSchema.Points)
            {
                AppendNumber(sb, reference, question.Points);
                continue;
            }

            if (!values.TryGetValue(header, out var value) || string.IsNullOrEmpty(value))
                continue;   // omit empty cells, exactly as Excel itself does

            var style = header is "Prompt" or "Extra" or "Hint" ? 2 : 0;
            AppendInlineString(sb, reference, value, style);
        }

        sb.AppendLine("</row>");
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

    // --- Guide sheet --------------------------------------------------------

    private static string GuideSheetXml()
    {
        var sb = new StringBuilder();

        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sb.AppendLine($"""<worksheet xmlns="{SpreadsheetMlNamespace}">""");
        sb.AppendLine("""<cols><col min="1" max="1" width="34" customWidth="1"/><col min="2" max="2" width="96" customWidth="1"/></cols>""");
        sb.AppendLine("<sheetData>");

        var row = 1;
        foreach (var (heading, body) in QuizSheetSchema.Guide)
        {
            sb.Append($"""<row r="{row}">""");
            AppendInlineString(sb, CellRef(0, row), heading, styleIndex: 1);
            AppendInlineString(sb, CellRef(1, row), body, styleIndex: 2);
            sb.AppendLine("</row>");

            row++;
        }

        sb.AppendLine("</sheetData>");
        sb.AppendLine("</worksheet>");

        return sb.ToString();
    }

    // --- Cells --------------------------------------------------------------

    /// <summary>
    /// Inline strings rather than a sharedStrings table. A shared table is
    /// smaller for repetitive data, but it means every cell holds an index into
    /// a second part that has to stay in step -- an off-by-one there corrupts
    /// the whole sheet silently. Quiz prompts barely repeat, so the saving is
    /// theoretical and the risk is not.
    /// </summary>
    private static void AppendInlineString(StringBuilder sb, string reference, string value, int styleIndex)
    {
        var style = styleIndex == 0 ? string.Empty : $""" s="{styleIndex}" """.TrimEnd();

        sb.Append($"""<c r="{reference}"{style} t="inlineStr"><is><t xml:space="preserve">""");
        sb.Append(Esc(Clean(value)));
        sb.Append("</t></is></c>");
    }

    private static void AppendNumber(StringBuilder sb, string reference, double value)
        => sb.Append($"""<c r="{reference}"><v>{value.ToString("R", CultureInfo.InvariantCulture)}</v></c>""");

    /// <summary>
    /// Zero-based column index to an Excel reference: 0 -> A1, 26 -> AA1.
    ///
    /// Not decorative. Excel uses r= to place the cell, and the importer reads
    /// it rather than counting elements -- because Excel omits empty cells, so
    /// position in the file does not imply position in the row.
    /// </summary>
    private static string CellRef(int columnIndex, int rowNumber)
    {
        var name = string.Empty;
        var n = columnIndex;

        // Bijective base-26: there is no "zero" digit, so A=1..Z=26 and the
        // remainder has to be taken before the shift, not after.
        while (true)
        {
            name = (char)('A' + (n % 26)) + name;
            n = (n / 26) - 1;

            if (n < 0) break;
        }

        return name + rowNumber.ToString(CultureInfo.InvariantCulture);
    }

    private static string Clean(string text)
    {
        if (text.All(IsXmlSafe)) return text;

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            if (IsXmlSafe(c)) sb.Append(c);

        return sb.ToString();
    }

    private static bool IsXmlSafe(char c) =>
        c is '\t' or '\n' or '\r'
        || (c >= 0x20 && c <= 0xD7FF)
        || (c >= 0xE000 && c <= 0xFFFD);

    private static string Esc(string text) => SecurityElement.Escape(text) ?? string.Empty;

    private static string TypeName(Question question) => question switch
    {
        MultipleChoiceSingleQuestion => "MultipleChoiceSingle",
        MultipleChoiceMultipleQuestion => "MultipleChoiceMultiple",
        TrueFalseQuestion => "TrueFalse",
        ShortAnswerQuestion => "ShortAnswer",
        FillInTheBlankQuestion => "FillInTheBlank",
        MatchingQuestion => "Matching",
        EssayQuestion => "Essay",
        SequenceQuestion => "Sequence",

        // Unreachable while every kind is listed above. Kept as Essay because
        // it is the one type the importer treats as hand-graded, so an
        // unrecognised question degrades to "needs a human" rather than being
        // silently auto-marked wrong.
        _ => "Essay",
    };
}
