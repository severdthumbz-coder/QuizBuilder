namespace QuizBuilder.Core.Models;

/// <summary>
/// How well the person recalled a card, shown as four buttons. Each maps to a
/// SuperMemo "quality" 0..5 that drives the SM-2 schedule. Again is a lapse
/// (quality below 3); the rest are passing grades of increasing confidence.
/// </summary>
public enum ReviewGrade
{
    /// <summary>Forgot it — resets the card and shows it again tomorrow.</summary>
    Again = 0,

    /// <summary>Recalled, but with difficulty.</summary>
    Hard = 1,

    /// <summary>Recalled correctly.</summary>
    Good = 2,

    /// <summary>Recalled easily — stretches the interval fastest.</summary>
    Easy = 3,
}

/// <summary>
/// The spaced-repetition state for a single card: how many times in a row it has
/// been recalled, how fast its interval grows (the ease factor), the current
/// interval in days, and when it was last reviewed. This is personal progress,
/// not quiz content — it lives in a per-user store, never in the .qbx.
///
/// The scheduling maths (SM-2) is modelled in tools/port/sm2_scheduler_port.py
/// and applied by <see cref="Services.Sm2Scheduler"/>.
/// </summary>
public sealed class ReviewState
{
    /// <summary>Which quiz this card belongs to (progress is scoped per quiz).</summary>
    public Guid QuizId { get; set; }

    /// <summary>The card's stable id (StudyCard.Id).</summary>
    public Guid CardId { get; set; }

    /// <summary>Consecutive successful recalls; resets to 0 on a lapse.</summary>
    public int Repetitions { get; set; }

    /// <summary>The SM-2 ease factor; starts at 2.5, never falls below 1.3.</summary>
    public double Ease { get; set; } = Sm2Defaults.StartEase;

    /// <summary>Days until the next review. 0 means brand new (never scheduled).</summary>
    public int IntervalDays { get; set; }

    /// <summary>
    /// The integer day index (days since the Unix epoch, UTC) of the last review,
    /// or null if never reviewed. A day index rather than a timestamp keeps
    /// "due today" comparisons free of time-of-day and time-zone surprises.
    /// </summary>
    public int? LastReviewedDay { get; set; }
}

/// <summary>SM-2 constants, shared by the scheduler and the model default.</summary>
public static class Sm2Defaults
{
    public const double StartEase = 2.5;
    public const double MinEase = 1.3;
}
