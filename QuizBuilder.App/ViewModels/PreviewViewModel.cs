using System.Collections.ObjectModel;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;

namespace QuizBuilder.App.ViewModels;

/// <summary>One question as shown in the preview.</summary>
public sealed class PreviewQuestionViewModel
{
    private readonly Func<string?, byte[]?> _imageResolver;

    public PreviewQuestionViewModel(CompiledQuestion compiled, bool showAnswers, Func<string?, byte[]?> imageResolver)
    {
        Compiled = compiled;
        ShowAnswers = showAnswers;
        _imageResolver = imageResolver;
    }

    public CompiledQuestion Compiled { get; }
    public bool ShowAnswers { get; }

    public bool HasImage => !string.IsNullOrEmpty(Compiled.Question.ImageRelativePath);

    /// <summary>Image bytes for display, resolved through the package service.</summary>
    public byte[]? ImageBytes => _imageResolver(Compiled.Question.ImageRelativePath);

    public int Number => Compiled.Number;
    public string Prompt => Compiled.Question.Prompt;
    public string KindDisplayName => Compiled.Question.KindDisplayName;
    public double Points => Compiled.Question.Points;

    public string PointsLabel => Points == 1 ? "1 point" : $"{Points:0.##} points";

    public string? Hint => Compiled.Question.Hint;
    public bool HasHint => !string.IsNullOrWhiteSpace(Hint);

    /// <summary>
    /// The answer as a printable line. Built here rather than in XAML because
    /// each type expresses its answer differently, and a DataTemplate per type
    /// for read-only text would be a lot of markup for no benefit.
    /// </summary>
    public string AnswerText => Compiled.Question switch
    {
        MultipleChoiceSingleQuestion q =>
            q.Choices.FirstOrDefault(c => c.IsCorrect)?.Text ?? "(no correct answer marked)",

        MultipleChoiceMultipleQuestion q =>
            q.Choices.Any(c => c.IsCorrect)
                ? string.Join(", ", q.Choices.Where(c => c.IsCorrect).Select(c => c.Text))
                : "(no correct answer marked)",

        TrueFalseQuestion q => q.CorrectAnswer ? "True" : "False",

        ShortAnswerQuestion q =>
            q.AcceptedAnswers.Count > 0
                ? string.Join("  /  ", q.AcceptedAnswers)
                : "(no accepted answer)",

        FillInTheBlankQuestion q =>
            q.Blanks.Count > 0
                ? string.Join("   ", q.Blanks.Select(b =>
                    $"{{{{{b.Ordinal}}}}} = {(b.AcceptedAnswers.Count > 0 ? string.Join(" / ", b.AcceptedAnswers) : "(none)")}"))
                : "(no blanks)",

        MatchingQuestion q =>
            string.Join("   ", q.Pairs.Select(p => $"{p.Left} -> {p.Right}")),

        SequenceQuestion q =>
            q.Items.Count > 0 ? string.Join(" -> ", q.Items) : "(no items)",

        EssayQuestion q =>
            string.IsNullOrWhiteSpace(q.RubricNotes) ? "(graded by hand)" : q.RubricNotes,

        _ => string.Empty,
    };

    /// <summary>Options a student would see, or empty for types without any.</summary>
    public IReadOnlyList<PreviewOptionViewModel> Options
    {
        get
        {
            switch (Compiled.Question)
            {
                case MultipleChoiceSingleQuestion q:
                    return q.Choices
                        .Select((c, i) => new PreviewOptionViewModel(Letter(i), c.Text, c.IsCorrect, ShowAnswers))
                        .ToList();

                case MultipleChoiceMultipleQuestion q:
                    return q.Choices
                        .Select((c, i) => new PreviewOptionViewModel(Letter(i), c.Text, c.IsCorrect, ShowAnswers))
                        .ToList();

                case TrueFalseQuestion q:
                    return new List<PreviewOptionViewModel>
                    {
                        new("A", "True", q.CorrectAnswer, ShowAnswers),
                        new("B", "False", !q.CorrectAnswer, ShowAnswers),
                    };

                case MatchingQuestion when Compiled.MatchingOptions is { } options:
                    // Pre-shuffled by the compiler so the preview and an
                    // exported paper cannot disagree about the order.
                    return options
                        .Select((text, i) => new PreviewOptionViewModel(Letter(i), text, false, false))
                        .ToList();

                case SequenceQuestion q:
                    // Items in the compiler's presentation order, so the preview
                    // shows the same shuffle a taker would see. Never marked
                    // correct: the answer is the order, shown on the answer line.
                    var presentation = Compiled.SequencePresentation
                        ?? Enumerable.Range(0, q.Items.Count).ToList();
                    return presentation
                        .Where(idx => idx >= 0 && idx < q.Items.Count)
                        .Select((sourceIndex, i) =>
                            new PreviewOptionViewModel(Letter(i), q.Items[sourceIndex], false, false))
                        .ToList();

                default:
                    return Array.Empty<PreviewOptionViewModel>();
            }
        }
    }

    public bool HasOptions => Options.Count > 0;

    /// <summary>The left column of a matching question; empty for other types.</summary>
    public IReadOnlyList<string> MatchingPrompts =>
        Compiled.Question is MatchingQuestion q
            ? q.Pairs.Select(p => p.Left).Where(l => !string.IsNullOrWhiteSpace(l)).ToList()
            : Array.Empty<string>();

    public bool HasMatchingPrompts => MatchingPrompts.Count > 0;

    /// <summary>
    /// How many ruled answer lines to draw for written types.
    /// </summary>
    public int WritingLineCount => Compiled.Question switch
    {
        ShortAnswerQuestion => 1,
        // Roughly ten words a line, floor of three so a short essay still looks
        // like an essay. Capped so a 5000-word suggestion does not render 500
        // lines and freeze the preview.
        EssayQuestion q => Math.Clamp(q.SuggestedWordCount / 10, 3, 40),
        _ => 0,
    };

    /// <summary>
    /// The lines as a sequence, because ItemsSource needs an IEnumerable. Bound
    /// to the int, WPF silently renders nothing rather than complaining -- the
    /// essay would just have no answer space and no error anywhere.
    /// </summary>
    public IEnumerable<int> WritingLines => Enumerable.Range(0, WritingLineCount);

    public bool HasWritingLines => WritingLineCount > 0;

    private static string Letter(int index) =>
        index < 26
            ? ((char)('A' + index)).ToString()
            // Past Z, keep numbering rather than wrapping to 'A' again, which
            // would print two options with the same label.
            : (index + 1).ToString();
}

public sealed class PreviewOptionViewModel
{
    public PreviewOptionViewModel(string label, string text, bool isCorrect, bool showAnswers)
    {
        Label = label;
        Text = text;
        IsCorrect = isCorrect;
        ShowAnswers = showAnswers;
    }

    public string Label { get; }
    public string Text { get; }
    public bool IsCorrect { get; }
    public bool ShowAnswers { get; }

    /// <summary>Only marked when the answer key is showing.</summary>
    public bool ShowAsCorrect => ShowAnswers && IsCorrect;
}

public sealed class PreviewSectionViewModel
{
    public PreviewSectionViewModel(CompiledSection compiled, bool showAnswers, Func<string?, byte[]?> imageResolver)
    {
        Title = compiled.Title;
        Questions = compiled.Questions
            .Select(q => new PreviewQuestionViewModel(q, showAnswers, imageResolver))
            .ToList();

        TotalPoints = compiled.TotalPoints;
    }

    public string Title { get; }
    public IReadOnlyList<PreviewQuestionViewModel> Questions { get; }
    public double TotalPoints { get; }

    public bool IsEmpty => Questions.Count == 0;

    public string PointsLabel => TotalPoints == 1 ? "1 point" : $"{TotalPoints:0.##} points";
}

/// <summary>
/// The Preview tab: the paper as a student would see it, or with the answers.
///
/// Everything comes from IQuizCompiler, which Publish will use too. If this tab
/// did its own shuffling, an exported PDF could differ from what was checked
/// here -- and nobody would find out until the papers were printed.
/// </summary>
public sealed class PreviewViewModel : ViewModelBase
{
    private readonly IQuizDocumentService _document;
    private readonly ISettingsService _settings;
    private readonly IQuizCompiler _compiler;
    private readonly IQuizPackageService _package;

    private bool _showAnswers;
    private int _seed = Environment.TickCount;
    private CompiledQuiz? _compiled;

    public PreviewViewModel(
        IQuizDocumentService document,
        ISettingsService settings,
        IQuizCompiler compiler,
        IQuizPackageService package)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _package = package ?? throw new ArgumentNullException(nameof(package));

        Sections = new ObservableCollection<PreviewSectionViewModel>();
        Warnings = new ObservableCollection<string>();

        RefreshCommand = new RelayCommand(Reshuffle);
        ShowStudentViewCommand = new RelayCommand(() => ShowAnswers = false);
        ShowAnswerKeyCommand = new RelayCommand(() => ShowAnswers = true);

        // Rebuild when the document changes -- but only while this tab is
        // actually on screen.
        //
        // The tabs are singletons toggled by Visibility, so this ViewModel stays
        // alive and subscribed while the user is typing on the Quiz Builder tab.
        // Rebuilding unconditionally meant every keystroke ran Compile() and
        // then cleared and refilled Sections, which tears down and regenerates
        // every question container in the visual tree -- 150+ subtrees on a
        // modest quiz, synchronously, inside the TextBox's setter. That is what
        // made typing lag.
        //
        // Compile() itself is cheap; the WPF teardown behind it is not.
        //
        // Deferring is safe because OnActivated already rebuilds on the way in:
        // the tab cannot be shown without a rebuild happening first.
        _document.DocumentChanged += (_, _) => RebuildOrDefer();
        _settings.SettingsChanged += (_, _) => RebuildOrDefer();

        Rebuild();
    }

    /// <summary>
    /// Whether this tab is on screen. The shell keeps every tab alive and
    /// toggles Visibility, so without this the preview does full rebuilds while
    /// the user is typing somewhere else entirely.
    /// </summary>
    private bool _isVisible;

    /// <summary>Set when a change arrived while hidden. Cleared by Rebuild.</summary>
    private bool _isStale = true;

    public ObservableCollection<PreviewSectionViewModel> Sections { get; }
    public ObservableCollection<string> Warnings { get; }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand ShowStudentViewCommand { get; }
    public RelayCommand ShowAnswerKeyCommand { get; }

    public bool ShowAnswers
    {
        get => _showAnswers;
        set
        {
            if (_showAnswers == value) return;

            _showAnswers = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowStudentView));
            OnPropertyChanged(nameof(ModeLabel));

            // Same seed: switching to the answer key must show the SAME paper,
            // not a freshly shuffled one.
            Rebuild();
        }
    }

    public bool ShowStudentView => !_showAnswers;

    public string ModeLabel => _showAnswers ? "Answer key" : "Student view";

    public string Title => _compiled?.Title ?? string.Empty;

    public string Description => _compiled?.Description ?? string.Empty;
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public int QuestionCount => _compiled?.QuestionCount ?? 0;
    public double TotalPoints => _compiled?.TotalPoints ?? 0;

    public string SummaryLine
    {
        get
        {
            var questions = QuestionCount == 1 ? "1 question" : $"{QuestionCount} questions";
            var points = TotalPoints == 1 ? "1 point" : $"{TotalPoints:0.##} points";

            var parts = new List<string> { questions, points };

            if (_compiled?.TimeLimitMinutes is { } limit)
                parts.Add($"{limit} minutes");

            return string.Join("  ·  ", parts);
        }
    }

    /// <summary>
    /// The pass mark against this paper, phrased in whichever unit the author
    /// chose. A bare "75%" is ambiguous on a weighted paper -- 75% of the
    /// questions and 75% of the marks are different bars.
    /// </summary>
    public string PassMarkLine
    {
        get
        {
            if (_compiled is null) return string.Empty;

            if (_compiled.PassMarkBasis == PassMarkBasis.QuestionCount)
            {
                if (_compiled.GradeableQuestionCount <= 0) return string.Empty;

                var label = _compiled.QuestionsToPass == 1 ? "question" : "questions";
                return $"Pass mark: {_compiled.PassPercentage}% of the questions  "
                       + $"({_compiled.QuestionsToPass} of {_compiled.GradeableQuestionCount} {label} correct)";
            }

            if (_compiled.TotalPoints <= 0) return string.Empty;

            return $"Pass mark: {_compiled.PassPercentage}% of the points  "
                   + $"({_compiled.PointsToPass:0.##} of {_compiled.TotalPoints:0.##})";
        }
    }

    public bool HasPassMark => !string.IsNullOrEmpty(PassMarkLine);

    /// <summary>Shown next to a disabled Reshuffle button, so it is not a mystery.</summary>
    public bool ShowReshuffleHint => !CanReshuffle;

    public bool HasWarnings => Warnings.Count > 0;
    public bool IsEmpty => Sections.Count == 0 || Sections.All(s => s.IsEmpty);

    /// <summary>
    /// New seed, new paper -- but only when something is actually random.
    ///
    /// With the default settings nothing is randomised, so a new seed produces
    /// a byte-identical paper and the button looks broken. Disabling it is only
    /// half an answer: a greyed button with no explanation is still a dead
    /// button. ReshuffleHint says why, and how to change it.
    /// </summary>
    public bool CanReshuffle =>
        _settings.Current.Quiz.RandomizeQuestionOrder ||
        _settings.Current.Quiz.RandomizeAnswerOrder ||
        _settings.Current.Quiz.SelectionMode == QuestionSelectionMode.ExactCountPerSection;

    public string ReshuffleHint => CanReshuffle
        ? "Draw a new random paper."
        : "Nothing is randomised, so every paper is the same. "
          + "Turn on question or answer randomisation in Settings to use this.";

    private void Reshuffle()
    {
        // Guarantee a different seed. Environment.TickCount has ~15ms
        // resolution on Windows, so two quick clicks can land on the same
        // value and silently produce the same paper -- which looks exactly
        // like the button not working.
        var next = _seed;
        while (next == _seed) next = Random.Shared.Next();

        _seed = next;
        Rebuild();
    }

    /// <summary>
    /// Called when the tab becomes visible. Settings live on another tab and
    /// raise no document event, so without this the preview would silently show
    /// a paper built under the old settings.
    /// </summary>
    public void OnActivated()
    {
        _isVisible = true;

        // Only rebuild if something actually changed while away. The flag is set
        // by every document and settings event, and starts true so the first
        // activation always builds.
        if (_isStale) Rebuild();
    }

    /// <summary>Called when the tab is hidden, so changes can be deferred.</summary>
    public void OnDeactivated() => _isVisible = false;

    private void RebuildOrDefer()
    {
        if (_isVisible)
        {
            Rebuild();
            return;
        }

        // Hidden: remember that the paper on screen is out of date. OnActivated
        // rebuilds regardless, so nothing has to be replayed -- this flag exists
        // only to make the intent legible.
        _isStale = true;
    }

    private void Rebuild()
    {
        _isStale = false;
        _compiled = _compiler.Compile(_document.Current, _settings.Current.Quiz, _seed);

        Sections.Clear();
        foreach (var section in _compiled.Sections)
            Sections.Add(new PreviewSectionViewModel(section, _showAnswers, _package.GetImage));

        Warnings.Clear();
        foreach (var warning in _compiled.Warnings)
            Warnings.Add(warning);

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(HasDescription));
        OnPropertyChanged(nameof(QuestionCount));
        OnPropertyChanged(nameof(TotalPoints));
        OnPropertyChanged(nameof(SummaryLine));
        OnPropertyChanged(nameof(PassMarkLine));
        OnPropertyChanged(nameof(HasPassMark));
        OnPropertyChanged(nameof(HasWarnings));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanReshuffle));
        OnPropertyChanged(nameof(ReshuffleHint));
        OnPropertyChanged(nameof(ShowReshuffleHint));

        RelayCommand.RaiseCanExecuteChanged();
    }
}
