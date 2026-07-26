using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// Writes a quiz as an .xlsx for bulk editing.
///
/// Takes the AUTHORED document rather than a compiled paper: the point is to
/// edit questions, so applying selection or shuffling would produce a sheet that
/// silently disagrees with the quiz it came from.
/// </summary>
public interface IExcelExporter
{
    /// <param name="stream">Written to but not closed -- the caller owns it.</param>
    void Write(Stream stream, QuizDocument document);
}
