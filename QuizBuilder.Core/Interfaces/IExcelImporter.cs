using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// The outcome of reading a spreadsheet.
///
/// Problems are carried alongside a successful result on purpose. Most real
/// files are ALMOST right -- one row missing a Type, one Points cell holding
/// "two" -- and refusing the whole import over that would be obstructive, while
/// importing silently would leave the author with a paper that is quietly wrong.
/// So: import what parses, and say exactly what did not.
/// </summary>
public sealed class ImportResult
{
    private ImportResult(bool success, QuizDocument? document, int questionCount,
                         string? error, IReadOnlyList<string> problems)
    {
        Success = success;
        Document = document;
        QuestionCount = questionCount;
        Error = error;
        Problems = problems;
    }

    public bool Success { get; }

    /// <summary>The imported quiz, or null when nothing could be read.</summary>
    public QuizDocument? Document { get; }

    public int QuestionCount { get; }

    /// <summary>Why the whole file could not be read. Null on success.</summary>
    public string? Error { get; }

    /// <summary>
    /// Per-row notes about what was skipped or assumed. Can be non-empty on a
    /// successful import -- that is the normal case for a hand-edited file.
    /// </summary>
    public IReadOnlyList<string> Problems { get; }

    public static ImportResult Succeeded(QuizDocument document, int questionCount, IReadOnlyList<string> problems)
        => new(true, document, questionCount, null, problems);

    public static ImportResult Failed(string error, IReadOnlyList<string>? problems = null)
        => new(false, null, 0, error, problems ?? Array.Empty<string>());
}

/// <summary>
/// Reads a quiz from an .xlsx.
///
/// Must cope with files this app did not write: Excel's own shared-string table,
/// rich text runs, omitted empty cells, and columns in any order.
/// </summary>
public interface IExcelImporter
{
    /// <param name="stream">Read from but not closed -- the caller owns it.</param>
    ImportResult Read(Stream stream);
}
