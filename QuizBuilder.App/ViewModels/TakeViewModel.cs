using System.Collections.ObjectModel;
using System.Globalization;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;

namespace QuizBuilder.App.ViewModels;

/// <summary>
/// One section the taker can include or exclude, shown before a sitting when the
/// grading scope is "choose at quiz time". Ticking is local UI state; the chosen
/// ids are read off these rows when the paper is compiled.
/// </summary>
public sealed class SectionChoiceViewModel : ViewModelBase
{
    private bool _isSelected = true;

    public SectionChoiceViewModel(Guid sectionId, string title, int questionCount)
    {
        SectionId = sectionId;
        Title = title;
        QuestionCount = questionCount;
    }

    public Guid SectionId { get; }
    public string Title { get; }
    public int QuestionCount { get; }

    public string CountLabel => QuestionCount == 1 ? "1 question" : $"{QuestionCount} questions";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;

            _isSelected = value;
            OnPropertyChanged();

            // A parent watches this to re-evaluate whether Start is allowed (at
            // least one section must stay ticked).
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? SelectionChanged;
}

/// <summary>
/// One paused sitting, as a row: when it was saved, how far in, and a way to
/// discard it. Resuming is driven from the view (it needs to build a sitting
/// window), so this row just carries the attempt and a discard callback.
/// </summary>
public sealed class PausedAttemptRowViewModel
{
    private readonly Action<Guid> _discard;

    public PausedAttemptRowViewModel(PausedAttempt attempt, Action<Guid> discard)
    {
        Attempt = attempt;
        _discard = discard;

        SavedAt = attempt.SavedAt.LocalDateTime.ToString("d MMM yyyy, h:mm tt");

        var answered = attempt.Sections.Sum(s => s.Questions.Count(q => !q.Answer.IsEmpty));
        var total = attempt.Sections.Sum(s => s.Questions.Count);
        Progress = $"{answered} of {total} answered";

        var elapsed = TimeSpan.FromSeconds(attempt.ElapsedSeconds);
        Elapsed = elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m spent"
            : $"{elapsed.Minutes}m {elapsed.Seconds}s spent";

        DiscardCommand = new RelayCommand(() => _discard(Attempt.Id));
    }

    public PausedAttempt Attempt { get; }
    public string SavedAt { get; }
    public string Progress { get; }
    public string Elapsed { get; }
    public RelayCommand DiscardCommand { get; }
}

/// <summary>One past sitting, as a row.</summary>
public sealed class AttemptRowViewModel
{
    public AttemptRowViewModel(AttemptRecord record)
    {
        Record = record;

        TakenAt = record.TakenAt.LocalDateTime.ToString("d MMM yyyy, HH:mm", CultureInfo.CurrentCulture);

        Score = record.Percentage is { } percentage
            ? $"{percentage:0.#}%"
            : "Not marked";

        Detail = $"{AttemptRecordBuilder.FormatPoints(record.ScoredPoints)} of "
                 + $"{AttemptRecordBuilder.FormatPoints(record.AutoGradedPoints)} points";

        Outcome = record.Passed switch
        {
            true => "Passed",
            false => "Not passed",

            // Null is not a failure: an all-essay paper has no automatic verdict.
            null => "Needs review",
        };

        var elapsed = record.Elapsed;
        Elapsed = elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m"
            : $"{elapsed.Minutes}m {elapsed.Seconds}s";

        if (record.TimedOut) Elapsed += " (timed out)";

        ReviewNote = record.QuestionsAwaitingReview > 0
            ? $"{record.QuestionsAwaitingReview} awaiting review"
            : string.Empty;

        HasReviewNote = record.QuestionsAwaitingReview > 0;
    }

    public AttemptRecord Record { get; }

    public string TakenAt { get; }
    public string Score { get; }
    public string Detail { get; }
    public string Outcome { get; }
    public string Elapsed { get; }
    public string ReviewNote { get; }
    public bool HasReviewNote { get; }

    public bool Passed => Record.Passed == true;
    public bool Failed => Record.Passed == false;
}

/// <summary>
/// The Take tab: start a sitting, and see how past ones went.
/// </summary>
public sealed class TakeViewModel : ViewModelBase
{
    private readonly IQuizDocumentService _document;
    private readonly ISettingsService _settings;
    private readonly IQuizCompiler _compiler;
    private readonly IAttemptHistoryService _history;
    private readonly IPausedAttemptService _paused;

    private bool _isStale = true;
    private bool _isVisible;

    public TakeViewModel(
        IQuizDocumentService document,
        ISettingsService settings,
        IQuizCompiler compiler,
        IAttemptHistoryService history,
        IPausedAttemptService paused)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _paused = paused ?? throw new ArgumentNullException(nameof(paused));

        Attempts = new ObservableCollection<AttemptRowViewModel>();
        PausedAttempts = new ObservableCollection<PausedAttemptRowViewModel>();

        // Same deferral as Preview and Publish: these tabs stay alive and
        // subscribed while the user types elsewhere, and rebuilding on every
        // keystroke is what made typing lag.
        _document.DocumentChanged += (_, _) => MarkStale();
        _history.HistoryChanged += (_, _) => MarkStale();
        _paused.PausedAttemptsChanged += (_, _) => MarkStale();

        // The section picker's visibility follows the grading-scope setting, so a
        // change there must refresh this tab too.
        _settings.SettingsChanged += (_, _) => MarkStale();

        ClearHistoryCommand = new RelayCommand(ClearHistory, () => Attempts.Count > 0);
    }

    public ObservableCollection<SectionChoiceViewModel> SectionChoices { get; } = new();

    public ObservableCollection<AttemptRowViewModel> Attempts { get; }
    public ObservableCollection<PausedAttemptRowViewModel> PausedAttempts { get; }
    public RelayCommand ClearHistoryCommand { get; }

    public bool CanTake => _document.Current.Sections.Any(s => s.Questions.Count > 0);

    /// <summary>
    /// True when the taker should pick sections before starting -- the scope is
    /// "choose at quiz time" and there is more than one section to choose between.
    /// With a single section there is nothing to choose, so the picker stays
    /// hidden and the sitting behaves normally.
    /// </summary>
    public bool NeedsSectionChoice =>
        _settings.Current.Quiz.GradingScope == GradingScope.SelectAtQuizTime
        && _document.Current.Sections.Count > 1;

    /// <summary>
    /// Whether Start is allowed. Normally it tracks CanTake; when the section
    /// picker is showing, at least one section must stay ticked (an empty
    /// selection would compile to a blank paper).
    /// </summary>
    public bool CanStart =>
        CanTake && (!NeedsSectionChoice || SectionChoices.Any(c => c.IsSelected));

    public bool HasHistory => Attempts.Count > 0;

    public bool HasPausedAttempts => PausedAttempts.Count > 0;

    public string QuizTitle => _document.Current.Title;

    public string SummaryLine
    {
        get
        {
            var questions = _document.Current.Sections.Sum(s => s.Questions.Count);
            if (questions == 0) return "Add some questions first.";

            var limit = _settings.Current.Quiz.TimeLimitMinutes;

            var timing = limit is > 0
                ? $"{limit} minute limit"
                : "no time limit";

            return $"{questions} question{(questions == 1 ? "" : "s")}, {timing}.";
        }
    }

    /// <summary>
    /// A fresh paper for each sitting, from a random seed.
    ///
    /// Not seed 0 like the exports: those want reproducibility so republishing
    /// an unchanged quiz produces an identical file. A sitting wants the
    /// opposite -- taking the same quiz twice should reshuffle, or the second
    /// attempt is a memory test of the first one's order.
    /// </summary>
    public CompiledQuiz CompilePaper()
    {
        // When the picker is in play, pass the ticked section ids so the compiler
        // includes only those. Otherwise pass null, the whole-quiz behaviour used
        // by every other scope and by the exports.
        var included = NeedsSectionChoice
            ? SectionChoices.Where(c => c.IsSelected).Select(c => c.SectionId).ToHashSet()
            : null;

        return _compiler.Compile(_document.Current, _settings.Current.Quiz, Random.Shared.Next(), included);
    }

    public Guid QuizId => _document.Current.Id;

    public QuizSettings Settings => _settings.Current.Quiz;

    private void MarkStale()
    {
        if (_isVisible) Refresh();
        else _isStale = true;
    }

    public void OnActivated()
    {
        _isVisible = true;
        if (_isStale) Refresh();
    }

    public void OnDeactivated() => _isVisible = false;

    public void Refresh()
    {
        _isStale = false;

        Attempts.Clear();

        foreach (var record in _history.ForQuiz(_document.Current.Id))
            Attempts.Add(new AttemptRowViewModel(record));

        RebuildSectionChoices();

        PausedAttempts.Clear();
        foreach (var attempt in _paused.ForQuiz(_document.Current.Id))
            PausedAttempts.Add(new PausedAttemptRowViewModel(attempt, DiscardPaused));

        OnPropertyChanged(nameof(CanTake));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(NeedsSectionChoice));
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HasPausedAttempts));
        OnPropertyChanged(nameof(QuizTitle));
        OnPropertyChanged(nameof(SummaryLine));

        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Rebuilds the section-picker rows from the current document, in display
    /// order. Called on refresh so adding, removing, or reordering sections is
    /// reflected. Every section starts ticked -- the default is "take all".
    /// </summary>
    private void RebuildSectionChoices()
    {
        foreach (var choice in SectionChoices)
            choice.SelectionChanged -= OnSectionSelectionChanged;

        SectionChoices.Clear();

        foreach (var section in _document.Current.SectionsInDisplayOrder())
        {
            var choice = new SectionChoiceViewModel(section.Id, section.Title, section.Questions.Count);
            choice.SelectionChanged += OnSectionSelectionChanged;
            SectionChoices.Add(choice);
        }
    }

    private void OnSectionSelectionChanged(object? sender, EventArgs e)
    {
        // A tick changed: Start may need to enable or disable.
        OnPropertyChanged(nameof(CanStart));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void DiscardPaused(Guid attemptId)
    {
        _paused.Remove(attemptId);
        // The service raises PausedAttemptsChanged -> MarkStale; refresh now so
        // the list updates immediately rather than on next activation.
        Refresh();
    }

    private void ClearHistory()
    {
        _history.ClearForQuiz(_document.Current.Id);
        Refresh();
    }
}
