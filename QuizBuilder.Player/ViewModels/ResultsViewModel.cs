using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuizBuilder.Player.Services;

namespace QuizBuilder.Player.ViewModels;

/// <summary>
/// Shows the graded outcome and offers to email it. The email opens the native
/// composer pre-filled with the recipient the taker entered on the identity
/// screen -- the app composes, the taker sends.
/// </summary>
public partial class ResultsViewModel : ObservableObject
{
    private readonly QuizSessionService _session;
    private readonly IResultsEmailService _email;

    public ResultsViewModel(QuizSessionService session, IResultsEmailService email)
    {
        _session = session;
        _email = email;
        Load();
    }

    [ObservableProperty] private string _quizTitle = "Quiz";
    [ObservableProperty] private string _takerName = string.Empty;
    [ObservableProperty] private string _scoreHeadline = string.Empty;
    [ObservableProperty] private string _scoreDetail = string.Empty;
    [ObservableProperty] private string? _passFailText;
    [ObservableProperty] private bool _isPass;
    [ObservableProperty] private bool _isFail;
    [ObservableProperty] private bool _hasPassFail;
    [ObservableProperty] private string? _reviewNote;
    [ObservableProperty] private string? _emailStatus;
    [ObservableProperty] private string _emailButtonText = "Email my results";

    private void Load()
    {
        QuizTitle = _session.QuizTitle;
        TakerName = _session.Identity?.FullName ?? string.Empty;

        var result = _session.LastResult;
        if (result is null) return;

        if (result.Percentage is { } pct)
        {
            ScoreHeadline = $"{pct:0.#}%";
            ScoreDetail = $"{result.ScoredPoints:0.##} of {result.AutoGradedPoints:0.##} points";

            if (result.Passed is { } passed)
            {
                HasPassFail = true;
                IsPass = passed;
                IsFail = !passed;
                PassFailText = passed ? "PASS" : "FAIL";
            }
        }
        else
        {
            ScoreHeadline = "Pending";
            ScoreDetail = "This quiz needs manual review before a score is available.";
        }

        if (result.HasReviewItems)
        {
            ReviewNote = $"{result.QuestionsAwaitingReview} question(s) worth " +
                         $"{result.PointsAwaitingReview:0.##} points need manual grading " +
                         $"and are not included in the score above.";
        }

        if (_session.Identity is { } id)
            EmailButtonText = $"Email results to {id.Email}";
    }

    [RelayCommand]
    private async Task EmailResultsAsync()
    {
        var id = _session.Identity;
        var result = _session.LastResult;
        if (id is null || result is null) return;

        EmailStatus = "Opening your mail app…";

        var outcome = await _email.ComposeResultsAsync(id, QuizTitle, result);
        EmailStatus = outcome switch
        {
            EmailSendOutcome.Composed => "Your mail app is open with the results. Tap Send to finish.",
            EmailSendOutcome.NotSupported => "No mail app is set up on this device. Add one, then try again.",
            _ => "Couldn't open the mail app. Please try again.",
        };
    }

    [RelayCommand]
    private async Task TakeAnotherAsync()
    {
        _session.ClearQuiz();
        // Stack is identity -> home -> take -> results. Two pops lands on home;
        // identity is retained so the next quiz reuses the same taker details.
        await Shell.Current.GoToAsync("../..");
    }

    [RelayCommand]
    private async Task DoneAsync()
    {
        _session.ClearQuiz();
        // Pop all the way back to the identity root: results, take, home.
        await Shell.Current.GoToAsync("../../..");
    }
}
