using System.Text;

namespace QuizBuilder.Core.Services;

/// <summary>
/// RFC 4180 CSV read/write over a rectangular grid of strings.
///
/// This exists because the quiz sheet schema puts every repeated field in its
/// own column precisely so no in-cell delimiter can collide with content. A
/// naive string.Split(',') CSV layer would reintroduce exactly that collision
/// at the file boundary -- a short answer of "and/or" or "a, b" would silently
/// become two fields. So reading is a hand state machine that honours quoting,
/// and writing quotes a field only when it must.
///
/// Proven first in tools/port/csv_import_template_port.py. Keep the two in step.
///
/// Scope: this is a dumb grid codec. It knows nothing about headers or
/// questions -- the importer maps a row to a question by header name, shared
/// with the .xlsx path, so a reordered or extra column never shifts a field.
/// </summary>
public static class CsvGrid
{
    private const string RecordSeparator = "\r\n";

    /// <summary>Writes rows as RFC-4180 text, CRLF-terminated, minimal quoting.</summary>
    public static string Write(IReadOnlyList<IReadOnlyList<string>> grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var sb = new StringBuilder();

        foreach (var row in grid)
        {
            for (var i = 0; i < row.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(QuoteIfNeeded(row[i] ?? string.Empty));
            }

            sb.Append(RecordSeparator);
        }

        return sb.ToString();
    }

    private static string QuoteIfNeeded(string field)
    {
        // Quote iff the field contains a comma, a double quote, CR or LF.
        var needs = field.Contains(',') || field.Contains('"')
                    || field.Contains('\r') || field.Contains('\n');

        if (!needs) return field;

        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// Parses RFC-4180 text into a grid. Handles quoted fields, doubled quotes,
    /// embedded commas and newlines, CRLF or lone LF/CR line endings, and a
    /// final record with no trailing newline.
    /// </summary>
    public static List<List<string>> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();

        var inQuotes = false;
        var fieldStarted = false;   // tells a genuine empty trailing line from real data

        void EndField()
        {
            row.Add(field.ToString());
            field.Clear();
            fieldStarted = false;
        }

        void EndRow()
        {
            rows.Add(row);
            row = new List<string>();
        }

        var i = 0;
        var n = text.Length;

        while (i < n)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // A doubled quote inside a quoted field is one literal quote.
                    if (i + 1 < n && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                    i++;
                    continue;
                }

                field.Append(c);
                i++;
                continue;
            }

            // Not inside quotes.
            if (c == '"' && field.Length == 0)
            {
                inQuotes = true;
                fieldStarted = true;
                i++;
                continue;
            }

            if (c == ',')
            {
                EndField();
                fieldStarted = true;    // a comma implies at least one more field
                i++;
                continue;
            }

            if (c == '\r')
            {
                EndField();
                EndRow();

                // Swallow the LF of a CRLF pair; a lone CR is still a line end.
                i += (i + 1 < n && text[i + 1] == '\n') ? 2 : 1;
                continue;
            }

            if (c == '\n')
            {
                EndField();
                EndRow();
                i++;
                continue;
            }

            field.Append(c);
            fieldStarted = true;
            i++;
        }

        // A trailing field/row not terminated by a newline.
        if (field.Length > 0 || fieldStarted || row.Count > 0)
        {
            EndField();
            EndRow();
        }

        return rows;
    }
}
