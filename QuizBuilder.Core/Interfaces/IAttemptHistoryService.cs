using System.Text.Json.Serialization;

namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// One question as it was answered, flattened to text at grading time.
///
/// Text rather than a reference to the question, because the question can
/// change: an author who fixes a typo after an attempt would otherwise make the
/// old report show the new wording, or crash when a question is deleted. A
/// report of a past attempt should show what was actually on screen that day.
/// </summary>
public sealed class AttemptQuestionRecord
{
    // Plain get/set with defaults, NOT `required init`.
    //
    // This type is read off disk. System.Text.Json enforces `required`, so
    // adding one new required property in a later version would make every
    // history.json written by an earlier one fail to load -- the whole file,
    // not just the new field, and the user loses every attempt they ever made
    // the moment they upgrade.
    //
    // `required` is right for a type the compiler constructs (CompiledQuestion
    // keeps it). It is wrong for a file format, where the compiler cannot help
    // and the cost of being wrong is somebody else's data.

    public int Number { get; set; }
    public string Prompt { get; set; } = string.Empty;

    /// <summary>What they put. Empty means unanswered.</summary>
    public string GivenAnswer { get; set; } = string.Empty;

    /// <summary>What would have been right. Empty for an essay.</summary>
    public string CorrectAnswer { get; set; } = string.Empty;

    public double Scored { get; set; }
    public double Possible { get; set; }

    /// <summary>Null when the question was not automatically gradeable.</summary>
    public bool? IsCorrect { get; set; }

    public bool NeedsReview { get; set; }
}

/// <summary>One sitting, as stored.</summary>
public sealed class AttemptRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Which quiz. Keyed on QuizDocument.Id, which survives a .qbx round trip.</summary>
    public Guid QuizId { get; set; }

    /// <summary>Kept so history still reads sensibly if the quiz is renamed.</summary>
    public string QuizTitle { get; set; } = string.Empty;

    public DateTimeOffset TakenAt { get; set; }

    /// <summary>Null when nothing could be graded automatically -- an all-essay paper.</summary>
    public double? Percentage { get; set; }

    public bool? Passed { get; set; }

    public double ScoredPoints { get; set; }

    /// <summary>Points that could be graded. NOT the paper's total: essays are excluded.</summary>
    public double AutoGradedPoints { get; set; }

    public double PointsAwaitingReview { get; set; }
    public int QuestionsAwaitingReview { get; set; }

    /// <summary>
    /// How long the sitting took, in seconds.
    ///
    /// An int rather than a TimeSpan. System.Text.Json's TimeSpan support
    /// arrived in .NET 7 and this targets net8.0, so it would probably work --
    /// but "probably" is not a thing to put in a file format, and this could not
    /// be compiled to check. Seconds are unambiguous, human-readable in the
    /// file, and cannot be misparsed by a future reader.
    /// </summary>
    public int ElapsedSeconds { get; set; }

    /// <summary>Convenience over <see cref="ElapsedSeconds"/>; not serialised.</summary>
    [JsonIgnore]
    public TimeSpan Elapsed
    {
        get => TimeSpan.FromSeconds(ElapsedSeconds);
        set => ElapsedSeconds = (int)Math.Round(value.TotalSeconds);
    }

    public bool TimedOut { get; set; }

    public List<AttemptQuestionRecord> Questions { get; set; } = new();

    /// <summary>Questions marked wrong, for the report.</summary>
    public IEnumerable<AttemptQuestionRecord> Incorrect => Questions.Where(q => q.IsCorrect == false);
}

/// <summary>
/// Stores attempts so a quiz's history is there when the quiz is reopened.
///
/// Kept in history.json beside the executable, the same portability rule as
/// settings.json -- not inside the .qbx, which is the authored document and gets
/// shared: baking one person's scores into the file they hand out would be
/// wrong, and every save would rewrite it.
/// </summary>
public interface IAttemptHistoryService
{
    /// <summary>Attempts for one quiz, newest first.</summary>
    IReadOnlyList<AttemptRecord> ForQuiz(Guid quizId);

    void Add(AttemptRecord attempt);

    /// <summary>Forgets one attempt.</summary>
    void Remove(Guid attemptId);

    /// <summary>Forgets every attempt for a quiz.</summary>
    void ClearForQuiz(Guid quizId);

    void Load();
    void Save();

    event EventHandler? HistoryChanged;
}
