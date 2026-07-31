using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// A sitting saved partway through, to be resumed later. Unlike an
/// <see cref="AttemptRecord"/> -- which is a finished result -- this is a live
/// snapshot: the exact paper the taker saw and the answers entered so far.
///
/// It is deliberately self-contained. The paper is stored, not a seed to
/// recompile from, so resuming shows precisely what was paused even if the quiz
/// is later edited. An attempt is a moment in time; it should not shift under the
/// taker because the author changed a question afterwards.
/// </summary>
public sealed class PausedAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Which quiz, so the Take tab can list this quiz's paused sittings.</summary>
    public Guid QuizId { get; set; }

    /// <summary>Kept so the entry reads sensibly even if the quiz is renamed.</summary>
    public string QuizTitle { get; set; } = string.Empty;

    /// <summary>
    /// Normalized identity of who paused it: the taker's email, trimmed and
    /// lower-cased, so paused sittings are scoped per person on a shared device.
    /// Null on snapshots written before identity scoping; those legacy entries
    /// are shown to everyone so nothing silently disappears.
    /// </summary>
    public string? TakerEmailKey { get; set; }

    /// <summary>The taker's display name at the time, informational only.</summary>
    public string? TakerName { get; set; }

    public DateTimeOffset SavedAt { get; set; }

    /// <summary>
    /// Seconds spent in the sitting up to the save. The clock stops when paused,
    /// so this is time actually spent, not wall-clock since the save. Resuming a
    /// timed quiz continues from the remaining budget, however long the pause
    /// lasted -- saving must not cost the taker time.
    /// </summary>
    public int ElapsedSeconds { get; set; }

    /// <summary>The quiz's time limit in minutes, or null if untimed. Snapshotted
    /// so resume does not depend on the current settings.</summary>
    public int? TimeLimitMinutes { get; set; }

    /// <summary>Pass percentage at the time of the sitting, snapshotted for grading on resume.</summary>
    public double PassPercentage { get; set; }

    /// <summary>Whether pass/fail is on question count, snapshotted for the sitting.</summary>
    public bool PassOnQuestionCount { get; set; }

    /// <summary>The paper, section by section, exactly as presented.</summary>
    public List<PausedSection> Sections { get; set; } = new();
}

/// <summary>One section of a paused paper.</summary>
public sealed class PausedSection
{
    public Guid SourceSectionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<PausedQuestion> Questions { get; set; } = new();
}

/// <summary>
/// One question of a paused paper: the question itself (polymorphic, exactly as
/// it round-trips in a .qbx), the shuffled matching options if any, and the
/// answer entered so far.
/// </summary>
public sealed class PausedQuestion
{
    public int Number { get; set; }

    /// <summary>The question as presented. Uses the same $kind discriminator as the .qbx format.</summary>
    public Question Question { get; set; } = default!;

    /// <summary>The shuffled right-hand options for a matching question, or null.
    /// Persisted so resume shows the same order the taker was working against.</summary>
    public List<string>? MatchingOptions { get; set; }

    /// <summary>The presented order for a sequence question (a permutation of
    /// the item indices), or null. Persisted for the same reason as
    /// <see cref="MatchingOptions"/>: without it, a resumed sequence would fall
    /// back to the items' correct order and hand the taker the answer.</summary>
    public List<int>? SequencePresentation { get; set; }

    /// <summary>The answer entered so far. Never null; an untouched question has an empty one.</summary>
    public QuestionAnswer Answer { get; set; } = new();
}

/// <summary>Stores and retrieves paused sittings, mirroring the history service.</summary>
public interface IPausedAttemptService
{
    /// <summary>Paused sittings for one quiz, newest first.</summary>
    IReadOnlyList<PausedAttempt> ForQuiz(Guid quizId);

    /// <summary>This quiz's paused sittings for one taker (by normalized email
    /// key), including legacy entries that carry no key. Newest first.</summary>
    IReadOnlyList<PausedAttempt> ForQuizAndTaker(Guid quizId, string? takerEmailKey);

    /// <summary>Saves a paused sitting, replacing any earlier save with the same id.</summary>
    void Save(PausedAttempt attempt);

    /// <summary>Removes a paused sitting -- because it was resumed and finished, or discarded.</summary>
    void Remove(Guid attemptId);

    void Load();

    event EventHandler? PausedAttemptsChanged;
}
