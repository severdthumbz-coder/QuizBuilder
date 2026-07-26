using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;

namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// Turns an authored quiz plus settings into the paper a student would see.
///
/// Preview and Publish must both go through this. If either computed its own
/// question selection or shuffle, an exported PDF could differ from the
/// preview it was checked against -- a discrepancy nobody would notice until
/// the papers were already printed.
/// </summary>
public interface IQuizCompiler
{
    /// <param name="seed">
    /// Drives every random choice. The same seed reproduces the same paper
    /// exactly, which is what lets Preview repaint without reshuffling and lets
    /// Publish reproduce what was previewed.
    /// </param>
    /// <param name="includedSectionIds">
    /// When the grading scope is <see cref="GradingScope.SelectAtQuizTime"/>, only
    /// sections whose id is in this set are included -- the choice the taker made
    /// at the start of the sitting. Null (the default) applies no runtime filter,
    /// so every section is included, which is the behaviour for every other scope
    /// and for the exports and preview.
    /// </param>
    CompiledQuiz Compile(QuizDocument document, QuizSettings settings, int seed, IReadOnlySet<Guid>? includedSectionIds = null);
}
