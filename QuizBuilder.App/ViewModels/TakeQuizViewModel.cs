using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using QuizBuilder.Core.Theming;

namespace QuizBuilder.App.ViewModels;

/// <summary>
/// One question on the paper, with somewhere to put an answer.
///
/// The answer object is handed straight to the grader, so the shape the UI
/// writes and the shape the grader reads are the same object -- there is no
/// mapping step to get wrong.
/// </summary>
public sealed class TakeQuestionViewModel : ViewModelBase
{
    public TakeQuestionViewModel(CompiledQuestion compiled, Func<string?, byte[]?> imageResolver, QuestionAnswer? restore = null)
    {
        Compiled = compiled;
        Answer = restore ?? new QuestionAnswer();
        _imageResolver = imageResolver;

        Choices = new ObservableCollection<TakeChoiceViewModel>();
        Blanks = new ObservableCollection<TakeBlankViewModel>();
        Pairs = new ObservableCollection<TakePairViewModel>();
        SequenceItems = new ObservableCollection<TakeSequenceItemViewModel>();

        Build();

        // On resume the answer arrives pre-filled; the controls default to empty,
        // so push the saved values into them once built.
        if (restore is not null) SyncControlsFromAnswer();
    }

    private readonly Func<string?, byte[]?> _imageResolver;

    public CompiledQuestion Compiled { get; }
    public QuestionAnswer Answer { get; }

    public Question Question => Compiled.Question;

    public bool HasImage => !string.IsNullOrEmpty(Question.ImageRelativePath);
    public byte[]? ImageBytes => _imageResolver(Question.ImageRelativePath);
    public int Number => Compiled.Number;
    public string Prompt => Question.Prompt;
    public string? Hint => Question.Hint;
    public bool HasHint => !string.IsNullOrWhiteSpace(Question.Hint);

    public string PointsLabel =>
        $"{AttemptRecordBuilder.FormatPoints(Question.Points)} {(Question.Points == 1 ? "point" : "points")}";

    public ObservableCollection<TakeChoiceViewModel> Choices { get; }
    public ObservableCollection<TakeBlankViewModel> Blanks { get; }
    public ObservableCollection<TakePairViewModel> Pairs { get; }
    public ObservableCollection<TakeSequenceItemViewModel> SequenceItems { get; }

    public bool IsSingleChoice => Question is MultipleChoiceSingleQuestion;
    public bool IsMultiChoice => Question is MultipleChoiceMultipleQuestion;
    public bool IsTrueFalse => Question is TrueFalseQuestion;
    public bool IsShortAnswer => Question is ShortAnswerQuestion;
    public bool IsBlanks => Question is FillInTheBlankQuestion;
    public bool IsMatching => Question is MatchingQuestion;
    public bool IsSequence => Question is SequenceQuestion;
    public bool IsNumeric => Question is NumericQuestion;
    public bool IsDropdown => Question is DropdownQuestion;
    public bool IsEssay => Question is EssayQuestion;

    /// <summary>Unit label for a numeric question, shown after the input (may be null).</summary>
    public string? NumericUnit => (Question as NumericQuestion)?.Unit;

    /// <summary>
    /// The selected choice for a dropdown question, bound to the ComboBox's
    /// SelectedItem. Setting it selects that choice (which routes to ChoiceIndex
    /// through the same IsSelected path single-choice uses), so dropdown and
    /// single-choice share the answer mechanism exactly.
    /// </summary>
    public TakeChoiceViewModel? SelectedChoice
    {
        get => Choices.FirstOrDefault(c => c.IsSelected);
        set
        {
            if (value is not null) value.IsSelected = true;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Told to the taker up front. An essay that silently scores nothing would
    /// look like a bug; saying so makes it a design.
    /// </summary>
    public bool NeedsReview => IsEssay;

    /// <summary>
    /// A group name unique to this question, so radio buttons on different
    /// questions do not clear each other. Without it WPF treats every radio in
    /// the window as one group and only one answer sticks for the whole paper.
    /// </summary>
    public string RadioGroup => $"q{Compiled.Number}";

    public IReadOnlyList<string> MatchingOptions => Compiled.MatchingOptions ?? Array.Empty<string>();

    /// <summary>Short answer / essay text, bound straight through.</summary>
    public string? TextAnswer
    {
        get => IsEssay ? Answer.EssayAnswer : Answer.TextAnswer;
        set
        {
            if (IsEssay) Answer.EssayAnswer = value;
            else Answer.TextAnswer = value;

            OnPropertyChanged();
            AnswerChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool? BoolAnswer
    {
        get => Answer.BoolAnswer;
        set
        {
            Answer.BoolAnswer = value;
            OnPropertyChanged();
            AnswerChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>True/false is rendered as two radios, so it needs two booleans.</summary>
    public bool IsTrueSelected
    {
        get => Answer.BoolAnswer == true;
        set { if (value) BoolAnswer = true; }
    }

    public bool IsFalseSelected
    {
        get => Answer.BoolAnswer == false;
        set { if (value) BoolAnswer = false; }
    }

    public event EventHandler? AnswerChanged;

    public bool IsAnswered => !Answer.IsEmpty;

    private void Build()
    {
        switch (Question)
        {
            case MultipleChoiceSingleQuestion q:
                for (var i = 0; i < q.Choices.Count; i++)
                    Choices.Add(new TakeChoiceViewModel(this, i, q.Choices[i].Text, single: true));
                break;

            // Dropdown is single-choice; populate the same Choices collection.
            // The XAML renders a ComboBox instead of radio buttons, keyed on
            // IsDropdown, but the answer path (ChoiceIndex) is identical.
            case DropdownQuestion q:
                for (var i = 0; i < q.Choices.Count; i++)
                    Choices.Add(new TakeChoiceViewModel(this, i, q.Choices[i].Text, single: true));
                break;

            case MultipleChoiceMultipleQuestion q:
                for (var i = 0; i < q.Choices.Count; i++)
                    Choices.Add(new TakeChoiceViewModel(this, i, q.Choices[i].Text, single: false));
                break;

            case FillInTheBlankQuestion q:
                var ordered = q.Blanks.OrderBy(b => b.Ordinal).ToList();

                // Position, not Ordinal: the grader indexes the ordered list by
                // position, so the UI must key answers the same way or every
                // blank is scored against the wrong one.
                for (var i = 0; i < ordered.Count; i++)
                    Blanks.Add(new TakeBlankViewModel(this, i, ordered[i].Ordinal));
                break;

            case MatchingQuestion q:
                for (var i = 0; i < q.Pairs.Count; i++)
                    Pairs.Add(new TakePairViewModel(this, i, q.Pairs[i].Left));
                break;

            case SequenceQuestion q:
                // Seed the on-screen list in the compiler's presentation order.
                // Each item carries its AUTHORED index; the taker reorders the
                // rows, and CommitSequenceOrder reads those indices top-to-bottom
                // into Answer.SequenceAnswer. The answer stays empty until the
                // first move, so an untouched sequence is "unanswered" like
                // every other type rather than silently graded on the shuffle.
                var presentation = Compiled.SequencePresentation
                    ?? Enumerable.Range(0, q.Items.Count).ToList();

                foreach (var sourceIndex in presentation)
                {
                    if (sourceIndex < 0 || sourceIndex >= q.Items.Count) continue;
                    SequenceItems.Add(new TakeSequenceItemViewModel(sourceIndex, q.Items[sourceIndex]));
                }
                break;
        }
    }

    internal void RaiseAnswerChanged()
    {
        OnPropertyChanged(nameof(IsAnswered));
        AnswerChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Moves a sequence item from one position to another and commits the new
    /// order. Called by the view's drag handler. Positions are clamped, so an
    /// out-of-range drop (e.g. past the last row) is a no-op rather than a throw.
    /// </summary>
    internal void MoveSequenceItem(int from, int to)
    {
        if (from < 0 || from >= SequenceItems.Count) return;
        to = Math.Clamp(to, 0, SequenceItems.Count - 1);
        if (from == to) return;

        SequenceItems.Move(from, to);
        CommitSequenceOrder();
    }

    /// <summary>
    /// Rewrites the whole answer from the current on-screen order. The grader
    /// needs a complete permutation of 0..n-1, so even a single move records
    /// every item's position, not just the one that moved.
    /// </summary>
    private void CommitSequenceOrder()
    {
        Answer.SequenceAnswer = SequenceItems.Select(i => i.SourceIndex).ToList();
        RaiseAnswerChanged();
    }

    /// <summary>
    /// Pushes the (pre-filled) Answer into the freshly built controls, for
    /// resume. Text and true/false read straight from Answer already, so they
    /// only need a notify; choices, blanks, and matches hold their own display
    /// state and must be set explicitly.
    /// </summary>
    private void SyncControlsFromAnswer()
    {
        foreach (var choice in Choices)
            choice.RestoreSelected(
                Answer.ChoiceIndex == choice.Index || Answer.ChoiceIndices.Contains(choice.Index));

        foreach (var blank in Blanks)
            if (Answer.BlankAnswers.TryGetValue(blank.Index, out var text))
                blank.RestoreText(text);

        foreach (var pair in Pairs)
            if (Answer.MatchAnswers.TryGetValue(pair.Index, out var value))
                pair.RestoreSelected(value);

        // Reorder the sequence rows to the saved arrangement. The answer already
        // holds the correct order, so this only reshuffles the display to match
        // -- it must not call CommitSequenceOrder, which would be a no-op here
        // but conceptually re-derives what we are restoring from.
        if (IsSequence && Answer.SequenceAnswer.Count == SequenceItems.Count)
        {
            var bySource = SequenceItems.ToDictionary(i => i.SourceIndex);
            if (Answer.SequenceAnswer.All(bySource.ContainsKey))
            {
                SequenceItems.Clear();
                foreach (var sourceIndex in Answer.SequenceAnswer)
                    SequenceItems.Add(bySource[sourceIndex]);
            }
        }

        // Text and true/false bind through Answer directly; nudge the bindings.
        OnPropertyChanged(nameof(TextAnswer));
        OnPropertyChanged(nameof(BoolAnswer));
        OnPropertyChanged(nameof(IsTrueSelected));
        OnPropertyChanged(nameof(IsFalseSelected));
        OnPropertyChanged(nameof(IsAnswered));
    }
}

public sealed class TakeChoiceViewModel : ViewModelBase
{
    private readonly TakeQuestionViewModel _parent;
    private readonly bool _single;
    private bool _isSelected;

    public TakeChoiceViewModel(TakeQuestionViewModel parent, int index, string text, bool single)
    {
        _parent = parent;
        _single = single;

        Index = index;
        Text = text;
    }

    public int Index { get; }
    public string Text { get; }
    public string RadioGroup => _parent.RadioGroup;

    /// <summary>Sets the display state on resume without re-writing Answer (already correct).</summary>
    internal void RestoreSelected(bool selected)
    {
        _isSelected = selected;
        OnPropertyChanged(nameof(IsSelected));
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;

            _isSelected = value;
            OnPropertyChanged();

            if (_single)
            {
                if (value) _parent.Answer.ChoiceIndex = Index;
            }
            else
            {
                if (value) _parent.Answer.ChoiceIndices.Add(Index);
                else _parent.Answer.ChoiceIndices.Remove(Index);
            }

            _parent.RaiseAnswerChanged();
        }
    }
}

public sealed class TakeBlankViewModel : ViewModelBase
{
    private readonly TakeQuestionViewModel _parent;
    private string _text = string.Empty;

    public TakeBlankViewModel(TakeQuestionViewModel parent, int index, int ordinal)
    {
        _parent = parent;

        Index = index;
        Label = $"{ordinal}.";
    }

    public int Index { get; }
    public string Label { get; }

    internal void RestoreText(string text)
    {
        _text = text;
        OnPropertyChanged(nameof(Text));
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value) return;

            _text = value;
            _parent.Answer.BlankAnswers[Index] = value;

            OnPropertyChanged();
            _parent.RaiseAnswerChanged();
        }
    }
}

public sealed class TakePairViewModel : ViewModelBase
{
    private readonly TakeQuestionViewModel _parent;
    private string? _selected;

    public TakePairViewModel(TakeQuestionViewModel parent, int index, string left)
    {
        _parent = parent;

        Index = index;
        Left = left;
    }

    public int Index { get; }
    public string Left { get; }

    public IReadOnlyList<string> Options => _parent.MatchingOptions;

    internal void RestoreSelected(string value)
    {
        _selected = value;
        OnPropertyChanged(nameof(Selected));
    }

    public string? Selected
    {
        get => _selected;
        set
        {
            if (_selected == value) return;

            _selected = value;

            if (value is null) _parent.Answer.MatchAnswers.Remove(Index);
            else _parent.Answer.MatchAnswers[Index] = value;

            OnPropertyChanged();
            _parent.RaiseAnswerChanged();
        }
    }
}

/// <summary>
/// One draggable row in a sequence question. It is a plain display item: it
/// carries the item's authored index (the answer-key domain) and its text, and
/// holds no answer state of its own. Reordering happens on the parent, which
/// reads these rows' <see cref="SourceIndex"/> values to build the answer.
/// </summary>
public sealed class TakeSequenceItemViewModel : ViewModelBase
{
    public TakeSequenceItemViewModel(int sourceIndex, string text)
    {
        SourceIndex = sourceIndex;
        Text = text;
    }

    /// <summary>The item's index in the authored (correct) order.</summary>
    public int SourceIndex { get; }

    public string Text { get; }
}

/// <summary>
/// A sitting: the paper, a countdown, and the result.
/// </summary>
public sealed class TakeQuizViewModel : ViewModelBase
{
    private readonly IQuizGrader _grader;
    private readonly IAttemptHistoryService _history;
    private readonly IThemeService _theme;
    private readonly QuizSettings _settings;
    private readonly Guid _quizId;

    /// <summary>
    /// Monotonic, unlike DateTime.Now.
    ///
    /// A wall clock moves when the system time changes and jumps an hour at a
    /// DST boundary: a 30-minute exam crossing 02:00 in autumn would suddenly
    /// have 90 minutes left, and in spring it would end the instant it started.
    /// </summary>
    private readonly Stopwatch _clock = new();

    private readonly DispatcherTimer _timer;

    private bool _isSubmitted;
    private string _timeRemaining = string.Empty;
    private AttemptResult? _result;

    public TakeQuizViewModel(
        CompiledQuiz quiz,
        Guid quizId,
        QuizSettings settings,
        IQuizGrader grader,
        IAttemptHistoryService history,
        IThemeService theme,
        Func<string?, byte[]?> imageResolver,
        int resumeElapsedSeconds = 0,
        Guid? pausedAttemptId = null,
        IReadOnlyList<QuestionAnswer>? restoreAnswers = null)
    {
        Quiz = quiz ?? throw new ArgumentNullException(nameof(quiz));
        _quizId = quizId;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _grader = grader ?? throw new ArgumentNullException(nameof(grader));
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));

        // Time already spent in this sitting before it was paused. The clock
        // measures only this session; adding the offset gives total time spent,
        // so a resumed timed quiz continues from the remaining budget rather than
        // restarting the countdown.
        _resumeOffset = TimeSpan.FromSeconds(Math.Max(0, resumeElapsedSeconds));

        // The paused-attempt id this sitting came from, so re-pausing updates the
        // same entry and finishing removes it. Null for a fresh sitting; a new id
        // is minted lazily the first time it is paused.
        _pausedAttemptId = pausedAttemptId;

        Questions = new ObservableCollection<TakeQuestionViewModel>(
            quiz.Sections.SelectMany(s => s.Questions).Select((q, i) =>
                new TakeQuestionViewModel(q, imageResolver, restoreAnswers?.ElementAtOrDefault(i))));

        foreach (var question in Questions)
            question.AnswerChanged += (_, _) => OnPropertyChanged(nameof(ProgressLabel));

        SubmitCommand = new RelayCommand(() => Submit(timedOut: false), () => !_isSubmitted);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => OnTick();

        _clock.Start();

        if (HasTimeLimit) _timer.Start();

        UpdateTimeRemaining();
    }

    private readonly TimeSpan _resumeOffset;
    private Guid? _pausedAttemptId;

    /// <summary>Total time spent in this sitting, including any before a pause.</summary>
    /// <summary>
    /// Captures the sitting as a <see cref="PausedAttempt"/>: the paper exactly as
    /// shown, every answer entered so far, and the total time spent. Stopping the
    /// clock here freezes elapsed time, so the pause itself costs the taker
    /// nothing. The id is stable across re-pauses, so re-saving updates one entry.
    /// </summary>
    public PausedAttempt CreatePausedSnapshot()
    {
        _clock.Stop();
        _timer.Stop();

        _pausedAttemptId ??= Guid.NewGuid();

        var sections = Quiz.Sections.Select(section => new PausedSection
        {
            SourceSectionId = section.SourceSectionId,
            Title = section.Title,
            Questions = section.Questions.Select(cq =>
            {
                var vm = Questions.First(q => ReferenceEquals(q.Compiled, cq));

                return new PausedQuestion
                {
                    Number = cq.Number,
                    Question = cq.Question,
                    MatchingOptions = cq.MatchingOptions?.ToList(),
                    SequencePresentation = cq.SequencePresentation?.ToList(),
                    Answer = vm.Answer,
                };
            }).ToList(),
        }).ToList();

        return new PausedAttempt
        {
            Id = _pausedAttemptId.Value,
            QuizId = _quizId,
            QuizTitle = Quiz.Title,
            SavedAt = DateTimeOffset.Now,
            ElapsedSeconds = (int)TotalElapsed.TotalSeconds,
            TimeLimitMinutes = Quiz.TimeLimitMinutes,
            PassPercentage = _settings.PassPercentage,
            PassOnQuestionCount = _settings.PassMarkBasis == PassMarkBasis.QuestionCount,
            Sections = sections,
        };
    }

    /// <summary>The paused-attempt id this sitting is tied to, once it has been paused.</summary>
    public Guid? PausedAttemptId => _pausedAttemptId;

    private TimeSpan TotalElapsed => _clock.Elapsed + _resumeOffset;

    public CompiledQuiz Quiz { get; }
    public ObservableCollection<TakeQuestionViewModel> Questions { get; }
    public RelayCommand SubmitCommand { get; }

    public string Title => Quiz.Title;
    public string Description => Quiz.Description;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Quiz.Description);

    public bool HasTimeLimit => Quiz.TimeLimitMinutes is > 0;

    public string TimeRemaining
    {
        get => _timeRemaining;
        private set
        {
            if (_timeRemaining == value) return;

            _timeRemaining = value;
            OnPropertyChanged();
        }
    }

    public string ProgressLabel
    {
        get
        {
            var answered = Questions.Count(q => q.IsAnswered);
            return $"{answered} of {Questions.Count} answered";
        }
    }

    /// <summary>Set once graded. The window switches to the results view on this.</summary>
    public AttemptResult? Result
    {
        get => _result;
        private set
        {
            _result = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSubmitted));
        }
    }

    public bool IsSubmitted => _result is not null;

    /// <summary>Built on submit; null until then.</summary>
    public ResultSummaryViewModel? Summary { get; private set; }

    /// <summary>Raised once the paper is graded, so the window can show the result.</summary>
    public event EventHandler<AttemptResult>? Submitted;

    private void OnTick()
    {
        UpdateTimeRemaining();

        if (!HasTimeLimit || _isSubmitted) return;

        if (Remaining() <= TimeSpan.Zero)
        {
            // Auto-submit rather than discarding the attempt or letting it run
            // on. Discarding is punitive and loses their work; letting it run
            // makes the limit meaningless. Everything unanswered scores zero,
            // exactly as it would on paper.
            Submit(timedOut: true);
        }
    }

    private TimeSpan Remaining()
    {
        if (Quiz.TimeLimitMinutes is not { } minutes || minutes <= 0) return TimeSpan.Zero;

        var left = TimeSpan.FromMinutes(minutes) - TotalElapsed;

        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    private void UpdateTimeRemaining()
    {
        if (!HasTimeLimit)
        {
            TimeRemaining = "No time limit";
            return;
        }

        var left = Remaining();

        TimeRemaining = $"{(int)left.TotalMinutes}:{left.Seconds:00}";
    }

    /// <summary>
    /// Grades the paper and records the attempt.
    ///
    /// Idempotent: the tick that hits zero and a taker pressing Submit at the
    /// same instant must not produce two attempts.
    /// </summary>
    public void Submit(bool timedOut)
    {
        if (_isSubmitted) return;

        _isSubmitted = true;

        // Stop the clock first. Otherwise the countdown keeps running behind the
        // results screen, reaches zero, and fires auto-submit against an attempt
        // that is already finished.
        _timer.Stop();
        _clock.Stop();

        RelayCommand.RaiseCanExecuteChanged();

        var answers = Questions.ToDictionary(q => q.Compiled, q => q.Answer);

        var result = _grader.Grade(Quiz, answers, _settings, TotalElapsed, timedOut);

        Summary = new ResultSummaryViewModel(result, _theme.Current);
        Result = result;

        try
        {
            _history.Add(AttemptRecordBuilder.Build(_quizId, Quiz.Title, result));
        }
        catch (Exception)
        {
            // The score is already on screen and correct. Failing to write the
            // history file must not take down the results the taker is reading.
        }

        Submitted?.Invoke(this, result);
    }

    /// <summary>Stops the timer when the window closes mid-attempt.</summary>
    public void Cancel()
    {
        _timer.Stop();
        _clock.Stop();
    }
}

/// <summary>
/// One wrong (or unmarked) question on the results screen.
/// </summary>
public sealed class ResultLineViewModel
{
    public ResultLineViewModel(AttemptQuestionRecord record)
    {
        Number = record.Number;
        Prompt = record.Prompt;
        CorrectAnswer = record.CorrectAnswer;

        // An unanswered question shows an em dash rather than nothing: a blank
        // line reads as a rendering fault, not as "you skipped this".
        GivenAnswerDisplay = string.IsNullOrWhiteSpace(record.GivenAnswer)
            ? "— nothing"
            : record.GivenAnswer;
    }

    public int Number { get; }
    public string Prompt { get; }
    public string GivenAnswerDisplay { get; }
    public string CorrectAnswer { get; }
}

/// <summary>
/// The results screen.
///
/// Every line here is written to be true of a paper that is PART essay: saying
/// "you scored 100%" when half the marks are unmarked would be a lie by
/// omission, so the score line always states what it is a percentage of.
/// </summary>
public sealed class ResultSummaryViewModel
{
    public ResultSummaryViewModel(AttemptResult result, ThemeTokens theme)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(theme);

        var record = AttemptRecordBuilder.Build(Guid.Empty, string.Empty, result);

        Incorrect = record.Incorrect.Select(q => new ResultLineViewModel(q)).ToList();

        AwaitingReview = record.Questions
            .Where(q => q.NeedsReview)
            .Select(q => new ResultLineViewModel(q))
            .ToList();

        HasIncorrect = Incorrect.Count > 0;
        HasReviewItems = AwaitingReview.Count > 0;

        if (result.Percentage is not { } percentage)
        {
            // Nothing could be marked -- an all-essay paper. A Congratulations
            // screen here would be absurd, and 0% would be a lie.
            Headline = "Answers recorded";
            HeadlineColor = theme.Colors.TextPrimary;
            ScoreLine = "This quiz has no questions that can be marked automatically.";
            DetailLine = string.Empty;
        }
        else
        {
            var passed = result.Passed == true;

            Headline = passed ? "Congratulations!" : "Not this time";
            HeadlineColor = passed ? theme.Colors.Success : theme.Colors.Error;

            ScoreLine = $"{percentage:0.#}%";

            DetailLine = result.HasReviewItems

                // The qualifier is the point: without it, "100%" implies the
                // whole paper was marked when it was not.
                ? $"{AttemptRecordBuilder.FormatPoints(result.ScoredPoints)} of "
                  + $"{AttemptRecordBuilder.FormatPoints(result.AutoGradedPoints)} points that could be marked automatically."
                : $"{AttemptRecordBuilder.FormatPoints(result.ScoredPoints)} of "
                  + $"{AttemptRecordBuilder.FormatPoints(result.AutoGradedPoints)} points.";
        }

        ReviewLine = result.QuestionsAwaitingReview == 1
            ? $"1 question ({AttemptRecordBuilder.FormatPoints(result.PointsAwaitingReview)} points) needs your review."
            : $"{result.QuestionsAwaitingReview} questions ({AttemptRecordBuilder.FormatPoints(result.PointsAwaitingReview)} points) need your review.";

        var elapsed = result.Elapsed;
        var time = elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m {elapsed.Seconds}s"
            : $"{elapsed.Minutes}m {elapsed.Seconds}s";

        TimeLine = result.TimedOut
            ? $"Time ran out after {time}. Unanswered questions scored nothing."
            : $"Finished in {time}.";
    }

    public string Headline { get; }

    /// <summary>A hex token from the theme, converted by HexToBrushConverter.</summary>
    public string HeadlineColor { get; }

    public string ScoreLine { get; }
    public string DetailLine { get; }
    public string ReviewLine { get; }
    public string TimeLine { get; }

    public IReadOnlyList<ResultLineViewModel> Incorrect { get; }
    public IReadOnlyList<ResultLineViewModel> AwaitingReview { get; }

    public bool HasIncorrect { get; }
    public bool HasReviewItems { get; }
}
