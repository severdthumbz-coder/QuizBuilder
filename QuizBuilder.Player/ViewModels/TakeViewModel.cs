using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuizBuilder.Player.Services;

namespace QuizBuilder.Player.ViewModels;

/// <summary>
/// Drives the sitting one question at a time: a progress indicator, Prev/Next,
/// and a Submit on the last question. Each question is shown via a
/// QuestionPresenter that writes directly into the shared answer set, so this VM
/// only sequences -- it never touches per-type answer shapes.
/// </summary>
public partial class TakeViewModel : ObservableObject
{
    private readonly QuizSessionService _session;

    public TakeViewModel(QuizSessionService session)
    {
        _session = session;
        Load();
    }

    private List<QuestionPresenter> _presenters = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressLabel))]
    [NotifyPropertyChangedFor(nameof(Progress))]
    [NotifyPropertyChangedFor(nameof(IsFirst))]
    [NotifyPropertyChangedFor(nameof(IsLast))]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private int _index;

    [ObservableProperty] private QuestionPresenter? _current;
    [ObservableProperty] private string _quizTitle = "Quiz";

    public int Count => _presenters.Count;
    public bool IsFirst => Index <= 0;
    public bool IsLast => Index >= Count - 1;

    public string ProgressLabel => Count == 0 ? string.Empty : $"Question {Index + 1} of {Count}";
    public double Progress => Count == 0 ? 0 : (double)(Index + 1) / Count;

    private void Load()
    {
        var take = _session.Take;
        if (take is null) return;

        QuizTitle = _session.QuizTitle;

        _presenters = take.Questions
            .Select(cq =>
            {
                var answer = take.AnswerFor(cq);
                var bytes = _session.Package?.GetImage(cq.Question.ImageRelativePath);
                return QuestionPresenter.Create(cq, answer, bytes);
            })
            .ToList();

        Index = 0;
        Current = _presenters.Count > 0 ? _presenters[0] : null;
    }

    private void ShowCurrent() =>
        Current = Index >= 0 && Index < _presenters.Count ? _presenters[Index] : null;

    private bool CanGoPrevious => !IsFirst;
    private bool CanGoNext => !IsLast;

    [RelayCommand(CanExecute = nameof(CanGoPrevious))]
    private void Previous()
    {
        if (IsFirst) return;
        Index--;
        ShowCurrent();
    }

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        if (IsLast) return;
        Index++;
        ShowCurrent();
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        var take = _session.Take;
        var unanswered = take is null
            ? 0
            : take.Questions.Count(q => take.AnswerFor(q).IsEmpty);

        if (unanswered > 0)
        {
            var proceed = await Shell.Current.DisplayAlertAsync(
                "Submit quiz?",
                $"{unanswered} question(s) are unanswered and will be scored as zero. Submit anyway?",
                "Submit", "Keep going");

            if (!proceed) return;
        }

        _session.Submit();
        await Shell.Current.GoToAsync("results");
    }
}
