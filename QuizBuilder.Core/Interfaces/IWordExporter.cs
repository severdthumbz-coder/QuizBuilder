using QuizBuilder.Core.Services;
using QuizBuilder.Core.Theming;

namespace QuizBuilder.Core.Interfaces;

public sealed class WordExportOptions
{
    /// <summary>Show the answer key. The student copy and the key differ only by this.</summary>
    public bool ShowAnswers { get; set; }

    /// <summary>
    /// Resolves an image path to its bytes, or null. Supplied by the caller
    /// (which holds the package service) so the exporter stays a pure function of
    /// its inputs. A null resolver, or a null return, means the question renders
    /// without its image rather than producing a broken document.
    /// </summary>
    public Func<string?, byte[]?>? ImageBytesResolver { get; set; }
}

/// <summary>
/// Writes a compiled quiz as a .docx.
///
/// Implemented against the OOXML format directly rather than through
/// DocumentFormat.OpenXml. That is not a preference for hand-rolling: a .docx
/// is a ZIP of XML parts and System.IO.Compression is already in the BCL, so
/// the dependency would buy type safety at the cost of being unverifiable in
/// the environment this is written in. Writing the parts directly means the
/// output can be unzipped and checked.
/// </summary>
public interface IWordExporter
{
    /// <param name="stream">
    /// Written to but not closed -- the caller owns it.
    /// </param>
    void Write(Stream stream, CompiledQuiz quiz, ThemeTokens theme, WordExportOptions options);
}
