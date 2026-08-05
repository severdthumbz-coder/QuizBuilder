using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Reads a quiz from an .xlsx.
///
/// This is the first thing in the app that consumes a file someone ELSE made,
/// which changes the standard entirely. An exporter only has to satisfy itself;
/// an importer has to survive whatever Excel emits, plus whatever a person
/// types by hand.
///
/// Specifically it handles, because real files do this:
///   - shared strings (t="s"), which is what Excel writes even though this app
///     writes inline strings
///   - rich text runs inside one string, which must be concatenated or styled
///     text arrives truncated
///   - OMITTED cells: Excel writes no element at all for an empty cell, so
///     reading cells in document order and zipping against headers shifts every
///     field after the first gap. Cells are placed by their r= reference.
///   - columns in any order, matched by header name rather than position
///
/// It reports failures per row rather than skipping them. A partial import that
/// claims success is worse than an error: the teacher prints a paper that is
/// quietly missing three questions.
/// </summary>
public sealed class ExcelImporter : IExcelImporter
{
    private static readonly XNamespace S =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static readonly XNamespace R =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public ImportResult Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return ReadArchive(archive);
        }
        catch (InvalidDataException)
        {
            // Not a zip at all -- a .xls, a .csv renamed, or a corrupt download.
            return ImportResult.Failed(
                "That file is not a valid .xlsx. If it came from an older Excel, "
                + "open it and use Save As to make a .xlsx first.");
        }
    }

    private ImportResult ReadArchive(ZipArchive archive)
    {
        var sheetPath = FindQuestionsSheetPath(archive);
        if (sheetPath is null)
            return ImportResult.Failed("Could not find a worksheet in that file.");

        var sheetEntry = archive.GetEntry(sheetPath);
        if (sheetEntry is null)
            return ImportResult.Failed($"The workbook points at {sheetPath}, but it is not in the file.");

        var sharedStrings = ReadSharedStrings(archive);

        using var sheetStream = sheetEntry.Open();
        var sheet = XDocument.Load(sheetStream);

        var rows = sheet.Descendants(S + "row").ToList();
        if (rows.Count == 0)
            return ImportResult.Failed("That sheet is empty.");

        var headerRow = rows[0];
        var headers = ReadHeaderRow(headerRow, sharedStrings);

        if (!headers.ContainsKey(Normalise(QuizSheetSchema.Type))
            || !headers.ContainsKey(Normalise(QuizSheetSchema.Prompt)))
        {
            return ImportResult.Failed(
                "That sheet has no Type or Prompt column. Export a quiz to Excel first "
                + "to see the expected layout.");
        }

        var document = new QuizDocument();
        var sections = new Dictionary<string, Section>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();

        foreach (var row in rows.Skip(1))
        {
            var cells = ReadRow(row, sharedStrings);
            if (cells.Values.All(string.IsNullOrWhiteSpace)) continue;   // blank row

            // The r= attribute, not the loop index: a deleted row leaves a gap
            // in the numbering, and an error that cites the wrong row number
            // sends the user to the wrong place.
            var rowNumber = (int?)row.Attribute("r") ?? 0;

            try
            {
                var question = BuildQuestion(cells, headers, rowNumber, problems);
                if (question is null) continue;

                var sectionTitle = Value(cells, headers, QuizSheetSchema.Section);
                if (string.IsNullOrWhiteSpace(sectionTitle)) sectionTitle = "Section 1";

                if (!sections.TryGetValue(sectionTitle, out var section))
                {
                    section = new Section { Title = sectionTitle };
                    sections[sectionTitle] = section;
                    document.Sections.Add(section);
                    document.SectionDisplayOrder.Add(section.Id);
                }

                section.Questions.Add(question);
            }
            catch (Exception ex)
            {
                problems.Add($"Row {rowNumber}: {ex.Message}");
            }
        }

        var count = document.Sections.Sum(s => s.Questions.Count);

        if (count == 0)
        {
            return problems.Count > 0
                ? ImportResult.Failed("No questions could be read.", problems)
                : ImportResult.Failed("That sheet has a header row but no questions.");
        }

        return ImportResult.Succeeded(document, count, problems);
    }

    // --- Workbook navigation ------------------------------------------------

    /// <summary>
    /// Finds the sheet named "Questions", falling back to the first sheet.
    ///
    /// The sheet is located through the relationship id, not by assuming
    /// sheet1.xml: Excel does not renumber parts when sheets are reordered or
    /// deleted, so the first sheet in the workbook is routinely sheet3.xml.
    /// </summary>
    private string? FindQuestionsSheetPath(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null) return null;

        XDocument workbook;
        using (var stream = workbookEntry.Open())
            workbook = XDocument.Load(stream);

        var sheets = workbook.Descendants(S + "sheet").ToList();
        if (sheets.Count == 0) return null;

        var target = sheets.FirstOrDefault(s =>
                         string.Equals((string?)s.Attribute("name"),
                                       QuizSheetSchema.QuestionsSheetName,
                                       StringComparison.OrdinalIgnoreCase))
                     ?? sheets[0];

        var relationshipId = (string?)target.Attribute(R + "id");
        if (relationshipId is null) return null;

        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (relsEntry is null) return null;

        XDocument rels;
        using (var stream = relsEntry.Open())
            rels = XDocument.Load(stream);

        var relationship = rels.Root?.Elements()
            .FirstOrDefault(e => (string?)e.Attribute("Id") == relationshipId);

        var target2 = (string?)relationship?.Attribute("Target");
        if (target2 is null) return null;

        return target2.StartsWith('/')
            ? target2.TrimStart('/')
            : "xl/" + target2.Replace("../", string.Empty);
    }

    private List<string> ReadSharedStrings(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return new List<string>();

        using var stream = entry.Open();
        var document = XDocument.Load(stream);

        // Concatenate every <t> in the item: Excel splits styled text into
        // multiple runs, so taking the first would truncate "Hello world" to
        // "Hello " when only one word is bold.
        return document.Descendants(S + "si")
            .Select(si => string.Concat(si.Descendants(S + "t").Select(t => t.Value)))
            .ToList();
    }

    // --- Rows and cells -----------------------------------------------------

    private Dictionary<string, int> ReadHeaderRow(XElement row, List<string> sharedStrings)
    {
        var headers = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var cell in row.Elements(S + "c"))
        {
            var text = CellText(cell, sharedStrings);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var key = Normalise(text);

            // First wins: a duplicated header is the user's problem, but
            // throwing would be unhelpful when the second one is empty.
            headers.TryAdd(key, ColumnIndex(cell));
        }

        return headers;
    }

    private Dictionary<int, string> ReadRow(XElement row, List<string> sharedStrings)
    {
        var cells = new Dictionary<int, string>();

        foreach (var cell in row.Elements(S + "c"))
            cells[ColumnIndex(cell)] = CellText(cell, sharedStrings);

        return cells;
    }

    /// <summary>
    /// The column index from a cell's r= reference ("B7" -> 1).
    ///
    /// This is the important one. Excel omits empty cells entirely, so a row
    /// with A, B and D filled contains three elements -- and reading them in
    /// order puts D's value where C's should be, shifting every field after it.
    /// The bug produces plausible data, which is why it has to be impossible
    /// rather than caught.
    /// </summary>
    private static int ColumnIndex(XElement cell)
    {
        var reference = (string?)cell.Attribute("r");
        if (string.IsNullOrEmpty(reference)) return 0;

        var index = 0;
        foreach (var c in reference)
        {
            if (!char.IsLetter(c)) break;

            index = index * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return index - 1;
    }

    /// <summary>
    /// A cell's text, whichever of the several ways it is stored.
    /// </summary>
    private static string CellText(XElement cell, List<string> sharedStrings)
    {
        var type = (string?)cell.Attribute("t");

        switch (type)
        {
            case "inlineStr":
                var inline = cell.Element(S + "is");
                return inline is null
                    ? string.Empty
                    : string.Concat(inline.Descendants(S + "t").Select(t => t.Value));

            case "s":
                var index = cell.Element(S + "v")?.Value;
                if (index is null) return string.Empty;

                // A shared-string index out of range means the parts are out of
                // step. Return empty rather than throwing: one broken cell
                // should not fail the whole import.
                return int.TryParse(index, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                       && i >= 0 && i < sharedStrings.Count
                    ? sharedStrings[i]
                    : string.Empty;

            case "str":     // formula result
            case "b":       // boolean, stored as 0/1
            case null:      // numeric
            default:
                return cell.Element(S + "v")?.Value ?? string.Empty;
        }
    }

    // --- Building questions -------------------------------------------------

    private static string Value(Dictionary<int, string> cells, Dictionary<string, int> headers, string header)
        => headers.TryGetValue(Normalise(header), out var column)
           && cells.TryGetValue(column, out var value)
            ? value.Trim()
            : string.Empty;

    private Question? BuildQuestion(
        Dictionary<int, string> cells, Dictionary<string, int> headers, int rowNumber, List<string> problems)
    {
        var typeText = Value(cells, headers, QuizSheetSchema.Type);
        var prompt = Value(cells, headers, QuizSheetSchema.Prompt);

        if (string.IsNullOrWhiteSpace(typeText) && string.IsNullOrWhiteSpace(prompt))
            return null;   // effectively blank

        if (string.IsNullOrWhiteSpace(typeText))
        {
            problems.Add($"Row {rowNumber}: no Type, so this question was skipped.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            problems.Add($"Row {rowNumber}: no Prompt, so this question was skipped.");
            return null;
        }

        var options = ReadIndexed(cells, headers, QuizSheetSchema.Option, QuizSheetSchema.MaxOptions);
        var corrects = ReadIndexed(cells, headers, QuizSheetSchema.Correct, QuizSheetSchema.MaxOptions);
        var matches = ReadIndexed(cells, headers, QuizSheetSchema.Match, QuizSheetSchema.MaxOptions);

        var question = CreateQuestion(typeText, options, corrects, matches, cells, headers, rowNumber, problems);
        if (question is null) return null;

        question.Prompt = prompt;

        var hint = Value(cells, headers, QuizSheetSchema.Hint);
        if (!string.IsNullOrWhiteSpace(hint)) question.Hint = hint;

        var pointsText = Value(cells, headers, QuizSheetSchema.Points);
        if (string.IsNullOrWhiteSpace(pointsText))
        {
            question.Points = 1;
        }
        else if (double.TryParse(pointsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var points)
                 && points >= 0)
        {
            question.Points = points;
        }
        else
        {
            // Invariant culture on purpose: the file format always stores 1.5
            // as "1.5", whatever the machine's locale, so parsing with the
            // current culture would read it as 15 on a de-DE machine.
            problems.Add($"Row {rowNumber}: could not read Points \"{pointsText}\", so it was set to 1.");
            question.Points = 1;
        }

        return question;
    }

    private static List<string> ReadIndexed(
        Dictionary<int, string> cells, Dictionary<string, int> headers, Func<int, string> header, int max)
    {
        var values = new List<string>();

        for (var i = 1; i <= max; i++)
            values.Add(Value(cells, headers, header(i)));

        return values;
    }

    private Question? CreateQuestion(
        string typeText,
        List<string> options,
        List<string> corrects,
        List<string> matches,
        Dictionary<int, string> cells,
        Dictionary<string, int> headers,
        int rowNumber,
        List<string> problems)
    {
        var type = Normalise(typeText);

        switch (type)
        {
            case "multiplechoicesingle":
            case "mcsingle":
            case "multiplechoice":
            case "mc":
            {
                var q = new MultipleChoiceSingleQuestion();
                AddChoices(q.Choices, options, corrects);

                if (q.Choices.Count == 0)
                {
                    problems.Add($"Row {rowNumber}: no options, so this question was skipped.");
                    return null;
                }

                var correctCount = q.Choices.Count(c => c.IsCorrect);
                if (correctCount == 0)
                    problems.Add($"Row {rowNumber}: no option marked TRUE, so no answer is set.");
                else if (correctCount > 1)
                {
                    // Keep the first and say so, rather than silently allowing a
                    // single-answer question with two answers.
                    problems.Add($"Row {rowNumber}: {correctCount} options marked TRUE on a single-answer "
                                 + "question, so only the first was kept.");

                    var seen = false;
                    foreach (var choice in q.Choices.Where(c => c.IsCorrect))
                    {
                        if (seen) choice.IsCorrect = false;
                        seen = true;
                    }
                }

                return q;
            }

            case "multiplechoicemultiple":
            case "mcmultiple":
            case "multipleanswer":
            {
                var q = new MultipleChoiceMultipleQuestion();
                AddChoices(q.Choices, options, corrects);

                if (q.Choices.Count == 0)
                {
                    problems.Add($"Row {rowNumber}: no options, so this question was skipped.");
                    return null;
                }

                if (!q.Choices.Any(c => c.IsCorrect))
                    problems.Add($"Row {rowNumber}: no option marked TRUE, so no answer is set.");

                return q;
            }

            case "truefalse":
            case "tf":
            {
                var text = corrects.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

                if (string.IsNullOrWhiteSpace(text))
                {
                    problems.Add($"Row {rowNumber}: no TRUE or FALSE in Correct 1, so it was set to True.");
                    return new TrueFalseQuestion { CorrectAnswer = true };
                }

                var parsed = ParseBool(text);
                if (parsed is null)
                {
                    problems.Add($"Row {rowNumber}: could not read \"{text}\" as TRUE or FALSE, so it was set to True.");
                    return new TrueFalseQuestion { CorrectAnswer = true };
                }

                return new TrueFalseQuestion { CorrectAnswer = parsed.Value };
            }

            case "shortanswer":
            case "short":
            {
                var answers = options.Where(o => !string.IsNullOrWhiteSpace(o)).ToList();

                if (answers.Count == 0)
                    problems.Add($"Row {rowNumber}: no accepted answers, so any answer will need marking by hand.");

                var q = new ShortAnswerQuestion();
                q.AcceptedAnswers.AddRange(answers);
                return q;
            }

            case "fillintheblank":
            case "fillblank":
            case "fill":
            {
                var q = new FillInTheBlankQuestion();

                var ordinal = 1;
                foreach (var answer in options)
                {
                    if (!string.IsNullOrWhiteSpace(answer))
                    {
                        var blank = new Blank { Ordinal = ordinal };
                        blank.AcceptedAnswers.Add(answer);
                        q.Blanks.Add(blank);
                    }

                    ordinal++;
                }

                if (q.Blanks.Count == 0)
                    problems.Add($"Row {rowNumber}: no blank answers were given.");

                return q;
            }

            case "matching":
            case "match":
            {
                var q = new MatchingQuestion();

                for (var i = 0; i < options.Count; i++)
                {
                    var left = options[i];
                    var right = i < matches.Count ? matches[i] : string.Empty;

                    if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right)) continue;

                    if (string.IsNullOrWhiteSpace(right))
                    {
                        problems.Add($"Row {rowNumber}: \"{left}\" has no Match {i + 1}, so the pair was skipped.");
                        continue;
                    }

                    q.Pairs.Add(new MatchPair { Left = left, Right = right });
                }

                for (var i = 1; i <= QuizSheetSchema.MaxDistractors; i++)
                {
                    var distractor = Value(cells, headers, QuizSheetSchema.Distractor(i));
                    if (!string.IsNullOrWhiteSpace(distractor)) q.Distractors.Add(distractor);
                }

                if (q.Pairs.Count == 0)
                {
                    problems.Add($"Row {rowNumber}: no complete pairs, so this question was skipped.");
                    return null;
                }

                return q;
            }

            case "sequence":
            case "order":
            case "ordering":
            {
                // Items are read straight out of the Option columns in sheet
                // order -- that order IS the correct answer, mirroring how the
                // exporter wrote them.
                var items = options.Where(o => !string.IsNullOrWhiteSpace(o)).ToList();

                if (items.Count < 2)
                {
                    problems.Add($"Row {rowNumber}: a sequence needs at least two items, so this row was skipped.");
                    return null;
                }

                var q = new SequenceQuestion();
                q.Items.AddRange(items);
                return q;
            }

            case "essay":
            case "longanswer":
            {
                var q = new EssayQuestion();

                var extra = Value(cells, headers, QuizSheetSchema.Extra);
                if (!string.IsNullOrWhiteSpace(extra)) q.RubricNotes = extra;

                return q;
            }

            case "dropdown":
            case "select":
            {
                // Identical shape to single-choice: options + one Correct.
                var q = new DropdownQuestion();
                AddChoices(q.Choices, options, corrects);

                if (q.Choices.Count == 0)
                {
                    problems.Add($"Row {rowNumber}: no options, so this question was skipped.");
                    return null;
                }

                var correctCount = q.Choices.Count(c => c.IsCorrect);
                if (correctCount == 0)
                    problems.Add($"Row {rowNumber}: no option marked TRUE, so no answer is set.");
                else if (correctCount > 1)
                {
                    problems.Add($"Row {rowNumber}: {correctCount} options marked TRUE on a dropdown "
                                 + "(single-answer) question, so only the first was kept.");
                    var seen = false;
                    foreach (var choice in q.Choices.Where(c => c.IsCorrect))
                    {
                        if (seen) choice.IsCorrect = false;
                        seen = true;
                    }
                }

                return q;
            }

            case "numeric":
            case "number":
            {
                // Target in Option 1, tolerance in Option 2, unit in Extra —
                // matching the exporter. A missing/blank tolerance means exact.
                var q = new NumericQuestion();

                var targetText = options.ElementAtOrDefault(0);
                if (string.IsNullOrWhiteSpace(targetText) ||
                    !double.TryParse(targetText.Trim(),
                        System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowLeadingSign,
                        System.Globalization.CultureInfo.InvariantCulture, out var target))
                {
                    problems.Add($"Row {rowNumber}: numeric question needs a number in Option 1, so it was skipped.");
                    return null;
                }
                q.Target = target;

                var tolText = options.ElementAtOrDefault(1);
                if (!string.IsNullOrWhiteSpace(tolText) &&
                    double.TryParse(tolText.Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var tol) && tol > 0)
                {
                    q.Tolerance = tol;
                }

                var unit = Value(cells, headers, QuizSheetSchema.Extra);
                if (!string.IsNullOrWhiteSpace(unit)) q.Unit = unit.Trim();

                return q;
            }

            default:
                problems.Add($"Row {rowNumber}: \"{typeText}\" is not a question type, so this row was skipped.");
                return null;
        }
    }

    private static void AddChoices(List<Choice> choices, List<string> options, List<string> corrects)
    {
        for (var i = 0; i < options.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(options[i])) continue;

            choices.Add(new Choice
            {
                Text = options[i],
                IsCorrect = i < corrects.Count && ParseBool(corrects[i]) == true,
            });
        }
    }

    /// <summary>
    /// Accepts what Excel and people actually put in a cell. Excel stores a real
    /// boolean as "1"/"0"; a person types TRUE, true, yes, or x.
    /// </summary>
    private static bool? ParseBool(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        return text.Trim().ToLowerInvariant() switch
        {
            "true" or "t" or "yes" or "y" or "1" or "x" => true,
            "false" or "f" or "no" or "n" or "0" => false,
            _ => null,
        };
    }

    /// <summary>
    /// Header and type matching that survives a human typing it: case, spaces
    /// and punctuation are all ignored, so "Multiple Choice Single",
    /// "multiple-choice single" and "MultipleChoiceSingle" are one thing.
    /// </summary>
    private static string Normalise(string text)
        => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
