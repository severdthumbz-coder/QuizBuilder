using System.Windows;
using System.Windows.Controls;
using QuizBuilder.App.ViewModels;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;

namespace QuizBuilder.App.Views;

public partial class TakeView : UserControl
{
    private readonly TakeViewModel _viewModel;
    private readonly IQuizGrader _grader;
    private readonly IAttemptHistoryService _history;
    private readonly IThemeService _theme;
    private readonly IQuizPackageService _package;
    private readonly IPausedAttemptService _paused;

    public TakeView(
        TakeViewModel viewModel,
        IQuizGrader grader,
        IAttemptHistoryService history,
        IThemeService theme,
        IQuizPackageService package,
        IPausedAttemptService paused)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _grader = grader ?? throw new ArgumentNullException(nameof(grader));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _package = package ?? throw new ArgumentNullException(nameof(package));
        _paused = paused ?? throw new ArgumentNullException(nameof(paused));

        InitializeComponent();
        DataContext = viewModel;

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) _viewModel.OnActivated();
            else _viewModel.OnDeactivated();
        };
    }

    private void OnStartClick(object sender, RoutedEventArgs e)
    {
        // The button binds IsEnabled to CanStart, but guard anyway: compiling a
        // paper with no sections chosen would produce an empty sitting.
        if (!_viewModel.CanStart) return;

        // Built here, not resolved from DI: a sitting is transient, with a
        // freshly compiled paper baked in. A singleton would hand the next
        // attempt the previous one's answers.
        var paper = _viewModel.CompilePaper();

        var takeViewModel = new TakeQuizViewModel(
            paper,
            _viewModel.QuizId,
            _viewModel.Settings,
            _grader,
            _history,
            _theme,
            _package.GetImage);

        RunSitting(takeViewModel);
    }

    private void OnResumeClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is PausedAttemptRowViewModel row)
            ResumePausedAttempt(row.Attempt);
    }

    /// <summary>
    /// Resumes a paused sitting: rebuilds the paper and answers from the snapshot,
    /// preloads the elapsed time, and runs it exactly like a fresh sitting.
    /// </summary>
    public void ResumePausedAttempt(PausedAttempt attempt)
    {
        var paper = PausedAttemptPaper.ToCompiledQuiz(attempt);
        var answers = PausedAttemptPaper.Answers(attempt);

        var takeViewModel = new TakeQuizViewModel(
            paper,
            attempt.QuizId,
            _viewModel.Settings,
            _grader,
            _history,
            _theme,
            _package.GetImage,
            resumeElapsedSeconds: attempt.ElapsedSeconds,
            pausedAttemptId: attempt.Id,
            restoreAnswers: answers);

        RunSitting(takeViewModel);
    }

    /// <summary>
    /// Shows the sitting window and, when it closes, reconciles the paused store:
    /// a finished sitting removes its paused entry (it became a completed
    /// attempt), while a "save &amp; continue later" persists a fresh snapshot.
    /// </summary>
    private void RunSitting(TakeQuizViewModel takeViewModel)
    {
        var window = new TakeQuizWindow(takeViewModel)
        {
            Owner = Window.GetWindow(this),
        };

        window.ShowDialog();

        if (takeViewModel.IsSubmitted)
        {
            // Finished: if this came from a paused entry, it is now a completed
            // attempt, so drop the paused copy.
            if (takeViewModel.PausedAttemptId is { } id)
                _paused.Remove(id);
        }
        else if (window.SaveRequested)
        {
            // Paused: persist the snapshot for later.
            _paused.Save(takeViewModel.CreatePausedSnapshot());
        }

        // The history service raises HistoryChanged on Add, but this tab is
        // hidden behind a modal while the attempt runs, so its deferral means
        // the refresh is pending rather than done. Refresh explicitly on the way
        // back rather than relying on visibility events firing for a dialog.
        _viewModel.Refresh();
    }
}
