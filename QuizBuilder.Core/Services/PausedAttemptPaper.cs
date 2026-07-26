using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Rebuilds the in-memory paper (a <see cref="CompiledQuiz"/>) from a saved
/// <see cref="PausedAttempt"/>, so a paused sitting resumes showing exactly what
/// was paused.
///
/// The snapshot is authoritative: nothing is recompiled and the live document is
/// not consulted. A paused attempt is a moment in time, and it should reappear
/// unchanged even if the quiz was edited in between.
/// </summary>
public static class PausedAttemptPaper
{
    /// <summary>The rebuilt paper.</summary>
    public static CompiledQuiz ToCompiledQuiz(PausedAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        var sections = attempt.Sections.Select(s => new CompiledSection
        {
            SourceSectionId = s.SourceSectionId,
            Title = s.Title,
            Questions = s.Questions.Select(q => new CompiledQuestion
            {
                Question = q.Question,
                Number = q.Number,
                MatchingOptions = q.MatchingOptions,
                SequencePresentation = q.SequencePresentation,
            }).ToList(),
        }).ToList();

        return new CompiledQuiz
        {
            Title = attempt.QuizTitle,
            Description = string.Empty,
            Sections = sections,
            TimeLimitMinutes = attempt.TimeLimitMinutes,

            // Pass mark comes from the snapshot, so a resumed sitting is graded on
            // the same terms it started under, even if the quiz's settings changed
            // while it was paused.
            PassPercentage = (int)Math.Round(attempt.PassPercentage),
            PassMarkBasis = attempt.PassOnQuestionCount
                ? PassMarkBasis.QuestionCount
                : PassMarkBasis.TotalPoints,

            // Seed is meaningless for a rebuilt paper -- it exists to reproduce a
            // freshly compiled one, and this paper comes from a stored snapshot,
            // not a recompile. Zero is a harmless placeholder.
            Seed = 0,
            Warnings = new List<string>(),
        };
    }

    /// <summary>
    /// The saved answers, in paper order, so the caller can drop them back into
    /// the question view models. Order matches ToCompiledQuiz's flattened
    /// question order, which is section-by-section as stored.
    /// </summary>
    public static IReadOnlyList<QuestionAnswer> Answers(PausedAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        return attempt.Sections
            .SelectMany(s => s.Questions)
            .Select(q => q.Answer)
            .ToList();
    }
}
