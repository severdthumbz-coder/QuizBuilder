using QuizBuilder.Core.Interfaces;
using System.Collections.ObjectModel;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;

namespace QuizBuilder.App.ViewModels.Questions;

public sealed class TrueFalseEditorViewModel : QuestionEditorViewModel
{
    private readonly TrueFalseQuestion _question;

    public TrueFalseEditorViewModel(TrueFalseQuestion question, Action notifyChanged, IQuizPackageService images)
        : base(question, notifyChanged, images)
        => _question = question;

    // Two properties rather than one bool, because RadioButton.IsChecked binds
    // one-way-per-button. Setting either updates the model and notifies both,
    // so the pair can never disagree.
    public bool AnswerIsTrue
    {
        get => _question.CorrectAnswer;
        set
        {
            if (!value || _question.CorrectAnswer) return;
            _question.CorrectAnswer = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AnswerIsFalse));
            MarkChanged();
        }
    }

    public bool AnswerIsFalse
    {
        get => !_question.CorrectAnswer;
        set
        {
            if (!value || !_question.CorrectAnswer) return;
            _question.CorrectAnswer = false;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AnswerIsTrue));
            MarkChanged();
        }
    }
}

/// <summary>One accepted answer string, wrapped so it can live in an editable list.</summary>
public sealed class AnswerRowViewModel : ViewModelBase
{
    private readonly Action _notifyChanged;
    private string _text;

    public AnswerRowViewModel(string text, Action notifyChanged)
    {
        _text = text;
        _notifyChanged = notifyChanged;
    }

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value)) _notifyChanged();
        }
    }
}

public sealed class ShortAnswerEditorViewModel : QuestionEditorViewModel
{
    private readonly ShortAnswerQuestion _question;

    public ShortAnswerEditorViewModel(ShortAnswerQuestion question, Action notifyChanged, IQuizPackageService images)
        : base(question, notifyChanged, images)
    {
        _question = question;

        Answers = new ObservableCollection<AnswerRowViewModel>(
            question.AcceptedAnswers.Select(a => new AnswerRowViewModel(a, SyncAnswers)));

        if (Answers.Count == 0) Answers.Add(new AnswerRowViewModel(string.Empty, SyncAnswers));

        AddAnswerCommand = new RelayCommand(() =>
        {
            Answers.Add(new AnswerRowViewModel(string.Empty, SyncAnswers));
            OnPropertyChanged(nameof(CanRemoveAnswers));
            SyncAnswers();
        });

        RemoveAnswerCommand = new RelayCommand(p =>
        {
            if (p is not AnswerRowViewModel row || !CanRemoveAnswers) return;
            Answers.Remove(row);
            OnPropertyChanged(nameof(CanRemoveAnswers));
            SyncAnswers();
        });
    }

    public ObservableCollection<AnswerRowViewModel> Answers { get; }

    public RelayCommand AddAnswerCommand { get; }
    public RelayCommand RemoveAnswerCommand { get; }

    public bool CanRemoveAnswers => Answers.Count > 1;

    public bool CaseSensitive
    {
        get => _question.CaseSensitive;
        set
        {
            if (_question.CaseSensitive == value) return;
            _question.CaseSensitive = value;
            OnPropertyChanged();
            MarkChanged();
        }
    }

    /// <summary>
    /// Rebuilds the model's list from the rows. Blank rows are dropped on the
    /// way in: a user who clears a box means "not this one", and persisting an
    /// empty accepted answer would match an empty submission.
    /// </summary>
    private void SyncAnswers()
    {
        _question.AcceptedAnswers = Answers
            .Select(a => a.Text?.Trim() ?? string.Empty)
            .Where(t => t.Length > 0)
            .ToList();

        MarkChanged();
    }

    public override IReadOnlyList<string> ValidationMessages
    {
        get
        {
            var messages = new List<string>(base.ValidationMessages);

            if (_question.AcceptedAnswers.Count == 0)
                messages.Add("No accepted answer yet.");

            return messages;
        }
    }
}

/// <summary>One blank's answer list, keyed by its {{n}} token.</summary>
public sealed class BlankViewModel : ViewModelBase
{
    private readonly Action _notifyChanged;

    public BlankViewModel(Blank model, Action notifyChanged)
    {
        Model = model;
        _notifyChanged = notifyChanged;

        Answers = new ObservableCollection<AnswerRowViewModel>(
            model.AcceptedAnswers.Select(a => new AnswerRowViewModel(a, Sync)));

        if (Answers.Count == 0) Answers.Add(new AnswerRowViewModel(string.Empty, Sync));

        AddAnswerCommand = new RelayCommand(() =>
        {
            Answers.Add(new AnswerRowViewModel(string.Empty, Sync));
            Sync();
        });

        RemoveAnswerCommand = new RelayCommand(p =>
        {
            if (p is AnswerRowViewModel row && Answers.Count > 1)
            {
                Answers.Remove(row);
                Sync();
            }
        });
    }

    public Blank Model { get; }

    public string Token => $"{{{{{Model.Ordinal}}}}}";

    public ObservableCollection<AnswerRowViewModel> Answers { get; }

    public RelayCommand AddAnswerCommand { get; }
    public RelayCommand RemoveAnswerCommand { get; }

    private void Sync()
    {
        Model.AcceptedAnswers = Answers
            .Select(a => a.Text?.Trim() ?? string.Empty)
            .Where(t => t.Length > 0)
            .ToList();

        _notifyChanged();
    }
}

public sealed class FillInTheBlankEditorViewModel : QuestionEditorViewModel
{
    private readonly FillInTheBlankQuestion _question;

    public FillInTheBlankEditorViewModel(FillInTheBlankQuestion question, Action notifyChanged, IQuizPackageService images)
        : base(question, notifyChanged, images)
    {
        _question = question;

        Blanks = new ObservableCollection<BlankViewModel>();

        AddBlankCommand = new RelayCommand(() =>
        {
            // Insert the token for the user rather than making them remember
            // the {{n}} syntax and keep the numbering straight themselves.
            Prompt = BlankSynchroniser.AppendNextToken(Prompt);
        });

        RebuildBlanks();
    }

    public ObservableCollection<BlankViewModel> Blanks { get; }

    public RelayCommand AddBlankCommand { get; }

    public bool CaseSensitive
    {
        get => _question.CaseSensitive;
        set
        {
            if (_question.CaseSensitive == value) return;
            _question.CaseSensitive = value;
            OnPropertyChanged();
            MarkChanged();
        }
    }

    private IReadOnlyList<string> _syncWarnings = Array.Empty<string>();

    /// <summary>The prompt is the source of truth for which blanks exist.</summary>
    protected override void OnPromptChanged() => RebuildBlanks();

    private void RebuildBlanks()
    {
        var result = BlankSynchroniser.Sync(_question.Prompt, _question.Blanks);

        _question.Blanks = result.Blanks;
        _syncWarnings = result.Warnings;

        Blanks.Clear();
        foreach (var blank in result.Blanks)
            Blanks.Add(new BlankViewModel(blank, MarkChanged));

        OnPropertyChanged(nameof(Blanks));
        OnPropertyChanged(nameof(HasBlanks));
        OnPropertyChanged(nameof(ValidationMessages));
        OnPropertyChanged(nameof(HasValidationMessages));
    }

    public bool HasBlanks => Blanks.Count > 0;

    public override IReadOnlyList<string> ValidationMessages
    {
        get
        {
            var messages = new List<string>(base.ValidationMessages);
            messages.AddRange(_syncWarnings);

            if (HasPrompt && Blanks.Count == 0)
                messages.Add("Add a blank with the button below, or type {{1}} in the text.");

            foreach (var blank in Blanks)
            {
                if (blank.Model.AcceptedAnswers.Count == 0)
                    messages.Add($"Blank {blank.Token} has no accepted answer.");
            }

            return messages;
        }
    }
}

public sealed class MatchPairViewModel : ViewModelBase
{
    private readonly Action _notifyChanged;

    public MatchPairViewModel(MatchPair model, Action notifyChanged)
    {
        Model = model;
        _notifyChanged = notifyChanged;
    }

    public MatchPair Model { get; }

    public string Left
    {
        get => Model.Left;
        set
        {
            if (Model.Left == value) return;
            Model.Left = value;
            OnPropertyChanged();
            _notifyChanged();
        }
    }

    public string Right
    {
        get => Model.Right;
        set
        {
            if (Model.Right == value) return;
            Model.Right = value;
            OnPropertyChanged();
            _notifyChanged();
        }
    }
}

public sealed class MatchingEditorViewModel : QuestionEditorViewModel
{
    private readonly MatchingQuestion _question;

    public MatchingEditorViewModel(MatchingQuestion question, Action notifyChanged, IQuizPackageService images)
        : base(question, notifyChanged, images)
    {
        _question = question;

        Pairs = new ObservableCollection<MatchPairViewModel>(
            question.Pairs.Select(p => new MatchPairViewModel(p, MarkChanged)));

        Distractors = new ObservableCollection<AnswerRowViewModel>(
            question.Distractors.Select(d => new AnswerRowViewModel(d, SyncDistractors)));

        if (Pairs.Count == 0)
        {
            AddPair();
            AddPair();
        }

        AddPairCommand = new RelayCommand(AddPair);
        RemovePairCommand = new RelayCommand(p =>
        {
            if (p is not MatchPairViewModel pair || !CanRemovePairs) return;
            _question.Pairs.Remove(pair.Model);
            Pairs.Remove(pair);
            OnPropertyChanged(nameof(CanRemovePairs));
            MarkChanged();
        });

        AddDistractorCommand = new RelayCommand(() =>
        {
            Distractors.Add(new AnswerRowViewModel(string.Empty, SyncDistractors));
            SyncDistractors();
        });

        RemoveDistractorCommand = new RelayCommand(p =>
        {
            if (p is not AnswerRowViewModel row) return;
            Distractors.Remove(row);
            SyncDistractors();
        });
    }

    public ObservableCollection<MatchPairViewModel> Pairs { get; }
    public ObservableCollection<AnswerRowViewModel> Distractors { get; }

    public RelayCommand AddPairCommand { get; }
    public RelayCommand RemovePairCommand { get; }
    public RelayCommand AddDistractorCommand { get; }
    public RelayCommand RemoveDistractorCommand { get; }

    /// <summary>Two pairs is the minimum that is actually a matching exercise.</summary>
    public bool CanRemovePairs => Pairs.Count > 2;

    private void AddPair()
    {
        var model = new MatchPair();
        _question.Pairs.Add(model);
        Pairs.Add(new MatchPairViewModel(model, MarkChanged));

        OnPropertyChanged(nameof(CanRemovePairs));
        MarkChanged();
    }

    private void SyncDistractors()
    {
        _question.Distractors = Distractors
            .Select(d => d.Text?.Trim() ?? string.Empty)
            .Where(t => t.Length > 0)
            .ToList();

        MarkChanged();
    }

    public override IReadOnlyList<string> ValidationMessages
    {
        get
        {
            var messages = new List<string>(base.ValidationMessages);

            if (Pairs.Any(p => string.IsNullOrWhiteSpace(p.Left) || string.IsNullOrWhiteSpace(p.Right)))
                messages.Add("Some pairs are incomplete.");

            // Duplicate right-hand values make a pair unanswerable: two lefts
            // would both legitimately match the same right.
            var rights = Pairs.Select(p => p.Right?.Trim() ?? string.Empty)
                              .Where(r => r.Length > 0)
                              .ToList();

            if (rights.Count != rights.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                messages.Add("Two pairs share the same right-hand value, so the match is ambiguous.");

            return messages;
        }
    }
}

public sealed class EssayEditorViewModel : QuestionEditorViewModel
{
    private readonly EssayQuestion _question;

    public EssayEditorViewModel(EssayQuestion question, Action notifyChanged, IQuizPackageService images)
        : base(question, notifyChanged, images)
        => _question = question;

    public string? RubricNotes
    {
        get => _question.RubricNotes;
        set
        {
            var normalised = string.IsNullOrWhiteSpace(value) ? null : value;
            if (_question.RubricNotes == normalised) return;

            _question.RubricNotes = normalised;
            OnPropertyChanged();
            MarkChanged();
        }
    }

    public int SuggestedWordCount
    {
        get => _question.SuggestedWordCount;
        set
        {
            var clamped = Math.Clamp(value, 0, 10000);
            if (_question.SuggestedWordCount == clamped) return;

            _question.SuggestedWordCount = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasWordCount));
            MarkChanged();
        }
    }

    /// <summary>Zero means "no suggested limit", so the UI hides the figure.</summary>
    public bool HasWordCount => _question.SuggestedWordCount > 0;
}

public sealed class SequenceEditorViewModel : QuestionEditorViewModel
{
    private readonly SequenceQuestion _question;

    public SequenceEditorViewModel(SequenceQuestion question, Action notifyChanged, IQuizPackageService images)
        : base(question, notifyChanged, images)
    {
        _question = question;

        Items = new ObservableCollection<AnswerRowViewModel>(
            question.Items.Select(i => new AnswerRowViewModel(i, SyncItems)));

        // Seed three empty rows for a new question. Two is the enforced minimum,
        // but a sequence of two collapses to a single swap, so the extra row is
        // a gentle nudge toward something worth ordering.
        while (Items.Count < 3)
            Items.Add(new AnswerRowViewModel(string.Empty, SyncItems));

        AddItemCommand = new RelayCommand(() =>
        {
            Items.Add(new AnswerRowViewModel(string.Empty, SyncItems));
            OnPropertyChanged(nameof(CanRemoveItems));
            SyncItems();
        });

        RemoveItemCommand = new RelayCommand(p =>
        {
            if (p is not AnswerRowViewModel row || !CanRemoveItems) return;
            Items.Remove(row);
            OnPropertyChanged(nameof(CanRemoveItems));
            SyncItems();
        });

        MoveUpCommand = new RelayCommand(p =>
        {
            if (p is not AnswerRowViewModel row) return;
            var i = Items.IndexOf(row);
            if (i <= 0) return;
            Items.Move(i, i - 1);
            SyncItems();
        });

        MoveDownCommand = new RelayCommand(p =>
        {
            if (p is not AnswerRowViewModel row) return;
            var i = Items.IndexOf(row);
            if (i < 0 || i >= Items.Count - 1) return;
            Items.Move(i, i + 1);
            SyncItems();
        });
    }

    public ObservableCollection<AnswerRowViewModel> Items { get; }

    public RelayCommand AddItemCommand { get; }
    public RelayCommand RemoveItemCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }

    /// <summary>Two items is the minimum that describes an order at all.</summary>
    public bool CanRemoveItems => Items.Count > 2;

    /// <summary>
    /// Rebuilds the model's Items from the rows, in the rows' current order --
    /// that order IS the correct answer the grader scores against. Blank rows
    /// are dropped: an empty item is not something a taker can meaningfully
    /// place, and it would distort the presentation shuffle.
    /// </summary>
    private void SyncItems()
    {
        _question.Items = Items
            .Select(i => i.Text?.Trim() ?? string.Empty)
            .Where(t => t.Length > 0)
            .ToList();

        MarkChanged();
    }

    public override IReadOnlyList<string> ValidationMessages
    {
        get
        {
            var messages = new List<string>(base.ValidationMessages);

            var items = _question.Items
                .Select(t => t?.Trim() ?? string.Empty)
                .Where(t => t.Length > 0)
                .ToList();

            if (items.Count < 2)
                messages.Add("A sequence needs at least two items to put in order.");

            // Duplicate item text makes the taker's arrangement ambiguous to
            // read back, even though the grader works on indices: two identical
            // items are indistinguishable on screen.
            if (items.Count != items.Distinct(StringComparer.OrdinalIgnoreCase).Count())
                messages.Add("Two items are identical, so their order cannot be told apart.");

            return messages;
        }
    }
}

/// <summary>
/// Editor for a numeric question: a target value, an optional tolerance, and an
/// optional unit label. The three are bound as text and parsed leniently so a
/// half-typed "-" or "." while the user is mid-entry doesn't reset the field.
/// </summary>
public sealed class NumericEditorViewModel : QuestionEditorViewModel
{
    private readonly NumericQuestion _question;

    public NumericEditorViewModel(NumericQuestion question, Action notifyChanged, IQuizPackageService images)
        : base(question, notifyChanged, images)
    {
        _question = question;
    }

    public string TargetText
    {
        get => _question.Target.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) && v != _question.Target)
            {
                _question.Target = v;
                OnPropertyChanged();
                MarkChanged();
            }
        }
    }

    public string ToleranceText
    {
        get => _question.Tolerance.ToString(System.Globalization.CultureInfo.InvariantCulture);
        set
        {
            if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0 && v != _question.Tolerance)
            {
                _question.Tolerance = v;
                OnPropertyChanged();
                MarkChanged();
            }
        }
    }

    public string? Unit
    {
        get => _question.Unit;
        set
        {
            if (_question.Unit == value) return;
            _question.Unit = value;
            OnPropertyChanged();
            MarkChanged();
        }
    }
}

/// <summary>
/// Editor for a dropdown question. Behaviourally identical to single-choice
/// (one correct option from a list); the only difference is that the taker sees
/// a dropdown. Reuses the shared choice-list editor and enforces exactly one
/// correct answer, exactly like the single-choice editor.
/// </summary>
public sealed class DropdownEditorViewModel : ChoiceListEditorViewModel
{
    public DropdownEditorViewModel(DropdownQuestion question, Action notifyChanged, IQuizPackageService images)
        : base(question, question.Choices, notifyChanged, images)
    {
        if (question.Choices.Count == 0)
        {
            AddChoiceCommand.Execute(null);
            AddChoiceCommand.Execute(null);
        }
    }

    protected override void OnChoiceMarkedCorrect(ChoiceViewModel choice)
    {
        foreach (var other in Choices)
        {
            if (ReferenceEquals(other, choice)) continue;
            other.ClearCorrectSilently();
        }
    }
}
