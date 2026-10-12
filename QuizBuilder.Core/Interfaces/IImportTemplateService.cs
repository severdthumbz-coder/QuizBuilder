using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// Produces a ready-to-edit <em>import template</em>: a file laid out in exactly
/// the shape <see cref="IExcelImporter"/> reads back, so a user can see the
/// required format, fill in their questions, and import the result.
///
/// The template is "both" by design: a worked example section (one illustrative
/// row per common question type) followed by the questions of the quiz currently
/// loaded, if any. The examples teach the layout; the real questions show it
/// applied to the user's own content. A new, empty quiz still gets a useful file
/// -- just the examples and the Guide.
///
/// Two formats, because they serve different users:
///   - .xlsx carries a second "Guide" sheet documenting every column and type,
///     and column widths tuned for reading. Richest, opens in Excel.
///   - .csv is a single flat table with no Guide sheet, but opens anywhere and
///     is the lingua franca for "import my questions". It round-trips through
///     the same importer.
/// </summary>
public interface IImportTemplateService
{
    /// <summary>
    /// Builds the synthetic template document: an example section plus a clone
    /// of <paramref name="currentQuiz"/>'s sections. Exposed so the xlsx path
    /// can hand it to the existing exporter and tests can inspect it directly.
    /// </summary>
    QuizDocument BuildTemplateDocument(QuizDocument currentQuiz);

    /// <summary>Writes the template as an .xlsx (Questions + Guide sheets).</summary>
    /// <param name="stream">Written to but not closed -- the caller owns it.</param>
    void WriteXlsx(Stream stream, QuizDocument currentQuiz);

    /// <summary>Writes the template as RFC-4180 CSV (single flat table).</summary>
    /// <param name="stream">Written to but not closed -- the caller owns it.</param>
    void WriteCsv(Stream stream, QuizDocument currentQuiz);
}
