using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <summary>
/// The SM-2 spaced-repetition scheduler. Pure, deterministic, side-effect-free:
/// given a card's current <see cref="ReviewState"/> and a grade, it returns the
/// next state. Mirrors tools/port/sm2_scheduler_port.py exactly — that port
/// proves the interval progression, ease drift and 1.3 floor, the lapse reset,
/// and the due-date arithmetic.
/// </summary>
public static class Sm2Scheduler
{
    /// <summary>The integer day index (days since Unix epoch, UTC) for a moment.</summary>
    public static int DayIndex(DateTimeOffset moment) =>
        (int)Math.Floor(moment.ToUniversalTime().ToUnixTimeSeconds() / 86400.0);

    /// <summary>Today's day index.</summary>
    public static int Today() => DayIndex(DateTimeOffset.UtcNow);

    /// <summary>Map a friendly grade to a SuperMemo quality (0..5).</summary>
    public static int QualityOf(ReviewGrade grade) => grade switch
    {
        ReviewGrade.Again => 2,   // < 3, so a lapse
        ReviewGrade.Hard => 3,
        ReviewGrade.Good => 4,
        ReviewGrade.Easy => 5,
        _ => 4,
    };

    /// <summary>The SM-2 ease update, clamped at the 1.3 floor. Applied on every
    /// review, including lapses (a forgotten card also loses ease).</summary>
    public static double UpdateEase(double ease, int quality)
    {
        var next = ease + (0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02));
        return next >= Sm2Defaults.MinEase ? next : Sm2Defaults.MinEase;
    }

    /// <summary>
    /// Apply one review at day <paramref name="today"/> and return the new state.
    /// The input state is not mutated. A grade of <see cref="ReviewGrade.Again"/>
    /// (quality &lt; 3) is a lapse: repetitions and interval reset, ease drops.
    /// </summary>
    public static ReviewState Review(ReviewState state, ReviewGrade grade, int today)
    {
        var quality = QualityOf(grade);

        int repetitions;
        int interval;

        if (quality < 3)
        {
            repetitions = 0;
            interval = 1;
        }
        else
        {
            interval = state.Repetitions switch
            {
                0 => 1,
                1 => 6,
                _ => (int)Math.Round(state.IntervalDays * state.Ease, MidpointRounding.AwayFromZero),
            };
            repetitions = state.Repetitions + 1;
        }

        return new ReviewState
        {
            QuizId = state.QuizId,
            CardId = state.CardId,
            Repetitions = repetitions,
            Ease = UpdateEase(state.Ease, quality),
            IntervalDays = interval,
            LastReviewedDay = today,
        };
    }

    /// <summary>Overload using today's date.</summary>
    public static ReviewState Review(ReviewState state, ReviewGrade grade) =>
        Review(state, grade, Today());

    /// <summary>
    /// Whether a card is due for review at <paramref name="today"/>. A card that
    /// has never been reviewed is due immediately.
    /// </summary>
    public static bool IsDue(ReviewState state, int today)
    {
        if (state.LastReviewedDay is not { } last) return true;
        return today >= last + state.IntervalDays;
    }

    /// <summary>
    /// Order due cards for a session: brand-new cards first, then the most
    /// overdue. Not-yet-due cards are excluded.
    /// </summary>
    public static IReadOnlyList<ReviewState> DueQueue(IEnumerable<ReviewState> states, int today)
    {
        return states
            .Where(s => IsDue(s, today))
            .OrderBy(s => s.LastReviewedDay is null ? 0 : 1)                       // new first
            .ThenByDescending(s => s.LastReviewedDay is { } last
                ? today - (last + s.IntervalDays)                                  // most overdue first
                : 0)
            .ToList();
    }
}
