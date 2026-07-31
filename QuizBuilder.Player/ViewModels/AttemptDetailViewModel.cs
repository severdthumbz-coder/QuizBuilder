using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;
using QuizBuilder.Player.Services;

namespace QuizBuilder.Player.ViewModels;

/// <summary>One question as it was answered, formatted for the detail list.</summary>
public sealed class AttemptQuestionRow
{
    public AttemptQuestionRow(AttemptQuestionRecord record)
    {
        Number = record.Number;
        Prompt = record.Prompt;

        GivenAnswer = string.IsNullOrWhiteSpace(record.GivenAnswer)
            ? "(no answer)"
            : record.GivenAnswer;

        // Essays have no single correct answer, so the builder leaves it empty;
        // showing a blank "Correct answer" line would look like a fault.
        CorrectAnswer = record.CorrectAnswer;
        HasCorrectAnswer = !string.IsNullOrWhiteSpace(record.CorrectAnswer);

        Points = $"{AttemptRecordBuilder.FormatPoints(record.Scored)} / " +
                 $"{AttemptRecordBuilder.FormatPoints(record.Possible)}";

        if (record.NeedsReview)
        {
            Status = "Needs review";
            IsCorrect = false;
            IsIncorrect = false;
            IsReview = true;
        }
        else if (record.IsCorrect == true)
        {
            Status = "Correct";
            IsCorrect = true;
        }
        else if (record.IsCorrect == false)
        {
            Status = "Incorrect";
            IsIncorrect = true;
        }
        else
        {
            // Not auto-gradeable and not flagged for review: no verdict to show.
            Status = string.Empty;
        }
    }

    public int Number { get; }
    public string Prompt { get; }
    public string GivenAnswer { get; }
    public string CorrectAnswer { get; }
    public bool HasCorrectAnswer { get; }
    public string Points { get; }
    public string Status { get; }
    public bool IsCorrect { get; }
    public bool IsIncorrect { get; }
    public bool IsReview { get; }
    public bool HasStatus => Status.Length > 0;
}

/// <summary>
/// The per-question breakdown of one past sitting, read from the record handed
/// over on the session. Everything shown is text captured at grading time, so
/// it reflects what was on screen that day even if the quiz was edited since.
/// </summary>
public partial class AttemptDetailViewModel : ObservableObject
{
    public AttemptDetailViewModel(QuizSessionService session)
    {
        var record = session.SelectedAttempt;
        if (record is null) return;

        QuizTitle = record.QuizTitle;
        TakenAt = record.TakenAt.LocalDateTime
            .ToString("d MMM yyyy, h:mm tt", CultureInfo.CurrentCulture);

        if (record.Percentage is { } pct)
        {
            ScoreHeadline = $"{pct.ToString("0.#", CultureInfo.CurrentCulture)}%";
            ScoreDetail = $"{AttemptRecordBuilder.FormatPoints(record.ScoredPoints)} of " +
                          $"{AttemptRecordBuilder.FormatPoints(record.AutoGradedPoints)} points";

            if (record.Passed is { } passed)
            {
                HasPassFail = true;
                IsPass = passed;
                IsFail = !passed;
                PassFailText = passed ? "PASS" : "FAIL";
            }
        }
        else
        {
            ScoreHeadline = "Pending review";
            ScoreDetail = "This sitting needs manual review before a score is available.";
        }

        if (record.QuestionsAwaitingReview > 0)
        {
            ReviewNote = $"{record.QuestionsAwaitingReview} question(s) worth " +
                         $"{AttemptRecordBuilder.FormatPoints(record.PointsAwaitingReview)} points " +
                         "need manual grading and are not included in the score above.";
        }

        Questions = record.Questions.Select(q => new AttemptQuestionRow(q)).ToList();
    }

    [ObservableProperty] private string _quizTitle = "Quiz";
    [ObservableProperty] private string _takenAt = string.Empty;
    [ObservableProperty] private string _scoreHeadline = string.Empty;
    [ObservableProperty] private string _scoreDetail = string.Empty;
    [ObservableProperty] private string? _passFailText;
    [ObservableProperty] private bool _hasPassFail;
    [ObservableProperty] private bool _isPass;
    [ObservableProperty] private bool _isFail;
    [ObservableProperty] private string? _reviewNote;

    public IReadOnlyList<AttemptQuestionRow> Questions { get; private set; } =
        new List<AttemptQuestionRow>();
}
