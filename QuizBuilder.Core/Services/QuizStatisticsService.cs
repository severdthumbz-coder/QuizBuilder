using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.Core.Services;

/// <summary>One question's difficulty across a quiz's attempts.</summary>
public sealed class QuestionDifficulty
{
    public QuestionDifficulty(string prompt, int timesWrong, int timesGraded)
    {
        Prompt = prompt;
        TimesWrong = timesWrong;
        TimesGraded = timesGraded;
    }

    public string Prompt { get; }

    /// <summary>Auto-graded appearances marked wrong.</summary>
    public int TimesWrong { get; }

    /// <summary>Auto-graded appearances (the denominator; essays never counted).</summary>
    public int TimesGraded { get; }

    /// <summary>0..1. TimesGraded is never zero for an entry that exists.</summary>
    public double MissRate => TimesGraded == 0 ? 0 : (double)TimesWrong / TimesGraded;
}

/// <summary>Aggregate statistics for one quiz (one taker, or everyone).</summary>
public sealed class QuizStatistics
{
    public int AttemptCount { get; init; }

    /// <summary>Attempts that had something auto-gradeable (a percentage).</summary>
    public int GradedAttemptCount { get; init; }

    /// <summary>Mean percentage over graded attempts. Null when none were graded.</summary>
    public double? AveragePercentage { get; init; }

    public double? BestPercentage { get; init; }

    /// <summary>The most recent attempt's percentage (by time). Null if all-essay.</summary>
    public double? LatestPercentage { get; init; }

    public int PassCount { get; init; }
    public int FailCount { get; init; }

    /// <summary>Mean sitting length in seconds. Null when there are no attempts.</summary>
    public int? AverageElapsedSeconds { get; init; }

    /// <summary>Questions ordered hardest-first (miss rate, then misses, then prompt).</summary>
    public IReadOnlyList<QuestionDifficulty> Hardest { get; init; } = Array.Empty<QuestionDifficulty>();

    /// <summary>Percentage of each graded attempt in time order, for a trend line.</summary>
    public IReadOnlyList<double> PercentageTrend { get; init; } = Array.Empty<double>();

    public bool HasAnyAttempts => AttemptCount > 0;

    public static QuizStatistics Empty { get; } = new();
}

/// <summary>
/// Computes per-quiz statistics from stored attempts. Pure over the records it is
/// given — the caller decides which attempts (a quiz's, optionally one taker's).
///
/// The aggregates and their edge cases (essays never counted as failures or as
/// "hardest", all-essay attempts contributing no percentage, miss-rate ordering)
/// were proven in tools/port/wrong_answers_stats_port.py first.
/// </summary>
public static class QuizStatisticsService
{
    public static QuizStatistics Compute(IReadOnlyList<AttemptRecord> attempts)
    {
        ArgumentNullException.ThrowIfNull(attempts);

        if (attempts.Count == 0) return QuizStatistics.Empty;

        var byTime = attempts.OrderBy(a => a.TakenAt).ToList();

        var graded = attempts.Where(a => a.Percentage is not null).ToList();
        var pcts = graded.Select(a => a.Percentage!.Value).ToList();

        var passCount = attempts.Count(a => a.Passed == true);
        var failCount = attempts.Count(a => a.Passed == false);

        var avgElapsed = (int)Math.Round(attempts.Average(a => a.ElapsedSeconds));

        // Per-question difficulty: only auto-graded appearances count, so essays
        // never inflate "hardest".
        var wrong = new Dictionary<string, int>(StringComparer.Ordinal);
        var gradedCount = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var attempt in attempts)
        {
            foreach (var rec in attempt.Questions)
            {
                if (rec.IsCorrect is null) continue;   // not auto-graded

                gradedCount[rec.Prompt] = gradedCount.GetValueOrDefault(rec.Prompt) + 1;
                if (rec.IsCorrect == false)
                    wrong[rec.Prompt] = wrong.GetValueOrDefault(rec.Prompt) + 1;
            }
        }

        var hardest = gradedCount.Keys
            .Select(p => new QuestionDifficulty(p, wrong.GetValueOrDefault(p), gradedCount[p]))
            .OrderByDescending(d => d.MissRate)
            .ThenByDescending(d => d.TimesWrong)
            .ThenBy(d => d.Prompt, StringComparer.Ordinal)
            .ToList();

        return new QuizStatistics
        {
            AttemptCount = attempts.Count,
            GradedAttemptCount = graded.Count,
            AveragePercentage = pcts.Count > 0 ? pcts.Average() : null,
            BestPercentage = pcts.Count > 0 ? pcts.Max() : null,
            LatestPercentage = byTime[^1].Percentage,
            PassCount = passCount,
            FailCount = failCount,
            AverageElapsedSeconds = avgElapsed,
            Hardest = hardest,
            PercentageTrend = byTime.Where(a => a.Percentage is not null)
                                    .Select(a => a.Percentage!.Value)
                                    .ToList(),
        };
    }
}
