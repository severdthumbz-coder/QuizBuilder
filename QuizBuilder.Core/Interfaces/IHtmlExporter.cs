using QuizBuilder.Core.Services;
using QuizBuilder.Core.Theming;

namespace QuizBuilder.Core.Interfaces;

public sealed class HtmlExportOptions
{
    /// <summary>Show the answer key. The student copy and the key differ only by this.</summary>
    public bool ShowAnswers { get; set; }

    /// <summary>
    /// Include the print bar. On for a file someone opens and prints; off when
    /// the HTML is destined for a website, where a stray Print button is noise.
    /// </summary>
    public bool IncludePrintButton { get; set; } = true;

    /// <summary>
    /// Resolves an image path to a data: URI, or null. Supplied by the caller
    /// (which holds the package service) so the exporter stays a pure function
    /// of its inputs rather than depending on the package service directly. Null
    /// resolver, or a null return, means the question renders without its image.
    /// </summary>
    public Func<string?, string?>? ImageDataUriResolver { get; set; }
}

/// <summary>
/// Renders a compiled quiz as one self-contained HTML file.
///
/// Self-contained is the point: the output gets emailed, uploaded, or carried
/// on a USB stick, and a page that needs a sibling stylesheet arrives broken
/// with no clue why.
///
/// This is also the PDF route. The browser's print engine paginates better
/// than a hand-rolled layout, honours the @media print rules, and carries no
/// licence obligation -- which the good .NET PDF libraries all do in one form
/// or another.
/// </summary>
public interface IHtmlExporter
{
    string Render(CompiledQuiz quiz, ThemeTokens theme, HtmlExportOptions options);
}
