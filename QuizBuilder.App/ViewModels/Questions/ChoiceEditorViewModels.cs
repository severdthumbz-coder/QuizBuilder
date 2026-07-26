using QuizBuilder.Core.Interfaces;
using System.Collections.ObjectModel;
using QuizBuilder.Core.Models;

namespace QuizBuilder.App.ViewModels.Questions;

/// <summary>One answer option row.</summary>
public sealed class ChoiceViewModel : ViewModelBase
{
    private readonly Action _notifyChanged;
    private readonly Action<ChoiceViewModel> _onCorrectSet;

    public ChoiceViewModel(Choice model, Action notifyChanged, Action<ChoiceViewModel> onCorrectSet)
    {
        Model = model;
        _notifyChanged = notifyChanged;
        _onCorrectSet = onCorrectSet;
    }

    public Choice Model { get; }

    public string Text
    {
        get => Model.Text;
        set
        {
            if (Model.Text == value) return;
            Model.Text = value;
            OnPropertyChanged();
            _notifyChanged();
        }
    }

    public bool IsCorrect
    {
        get => Model.IsCorrect;
        set
        {
            if (Model.IsCorrect == value) return;
            Model.IsCorrect = value;
            OnPropertyChanged();

            // Single-answer questions enforce exclusivity through this callback
            // rather than by binding to a RadioButton group. Radio groups in an
            // ItemsControl leak across items: WPF scopes GroupName by name, so
            // two questions' choice lists would fight each other.
            if (value) _onCorrectSet(this);

            _notifyChanged();
        }
    }

    /// <summary>
    /// Clears the correct flag without re-entering the exclusivity callback.
    /// Setting IsCorrect = false would work, but this states the intent: the
    /// sibling is being deselected BY the exclusivity rule, not by the user,
    /// so it must not trigger the rule again.
    /// </summary>
    internal void ClearCorrectSilently()
    {
        if (!Model.IsCorrect) return;

        Model.IsCorrect = false;
        OnPropertyChanged(nameof(IsCorrect));
    }
}

/// <summary>Shared behaviour for the two choice-list question types.</summary>
public abstract class ChoiceListEditorViewModel : QuestionEditorViewModel
{
    protected ChoiceListEditorViewModel(Question question, List<Choice> choices, Action notifyChanged, IQuizPackageService images)
        : base(question, notifyChanged, images)
    {
        ChoiceModels = choices;

        Choices = new ObservableCollection<ChoiceViewModel>(
            choices.Select(CreateChoiceViewModel));

        AddChoiceCommand = new RelayCommand(AddChoice);
        RemoveChoiceCommand = new RelayCommand(p =>
        {
            if (p is ChoiceViewModel choice) RemoveChoice(choice);
        });
    }

    protected List<Choice> ChoiceModels { get; }

    public ObservableCollection<ChoiceViewModel> Choices { get; }

    public RelayCommand AddChoiceCommand { get; }
    public RelayCommand RemoveChoiceCommand { get; }

    /// <summary>
    /// Two is the minimum that makes a choice question meaningful. Below that
    /// the remove button is disabled rather than hidden, so the control does
    /// not jump around as rows are added and removed.
    /// </summary>
    public bool CanRemoveChoices => Choices.Count > 2;

    private ChoiceViewModel CreateChoiceViewModel(Choice model)
        => new(model, MarkChanged, OnChoiceMarkedCorrect);

    protected virtual void OnChoiceMarkedCorrect(ChoiceViewModel choice) { }

    private void AddChoice()
    {
        var model = new Choice();
        ChoiceModels.Add(model);
        Choices.Add(CreateChoiceViewModel(model));

        OnPropertyChanged(nameof(CanRemoveChoices));
        MarkChanged();
    }

    private void RemoveChoice(ChoiceViewModel choice)
    {
        if (!CanRemoveChoices) return;

        ChoiceModels.Remove(choice.Model);
        Choices.Remove(choice);

        OnPropertyChanged(nameof(CanRemoveChoices));
        MarkChanged();
    }

    public override IReadOnlyList<string> ValidationMessages
    {
        get
        {
            var messages = new List<string>(base.ValidationMessages);

            if (Choices.Any(c => string.IsNullOrWhiteSpace(c.Text)))
                messages.Add("Some options have no text.");

            if (!Choices.Any(c => c.IsCorrect))
                messages.Add("No correct answer is marked.");

            return messages;
        }
    }
}

public sealed class MultipleChoiceSingleEditorViewModel : ChoiceListEditorViewModel
{
    public MultipleChoiceSingleEditorViewModel(MultipleChoiceSingleQuestion question, Action notifyChanged, IQuizPackageService images)
        : base(question, question.Choices, notifyChanged, images)
    {
        // A new question starts with two empty options rather than none: an
        // empty list gives the user nothing to react to, and every
        // single-answer question needs at least two.
        if (question.Choices.Count == 0)
        {
            AddChoiceCommand.Execute(null);
            AddChoiceCommand.Execute(null);
        }
    }

    /// <summary>
    /// Exactly one answer can be correct. Enforced here rather than with a
    /// RadioButton GroupName, which would leak between questions.
    /// </summary>
    protected override void OnChoiceMarkedCorrect(ChoiceViewModel choice)
    {
        foreach (var other in Choices)
        {
            if (ReferenceEquals(other, choice)) continue;
            other.ClearCorrectSilently();
        }
    }
}

public sealed class MultipleChoiceMultipleEditorViewModel : ChoiceListEditorViewModel
{
    private readonly MultipleChoiceMultipleQuestion _question;

    public MultipleChoiceMultipleEditorViewModel(MultipleChoiceMultipleQuestion question, Action notifyChanged, IQuizPackageService images)
        : base(question, question.Choices, notifyChanged, images)
    {
        _question = question;

        if (question.Choices.Count == 0)
        {
            AddChoiceCommand.Execute(null);
            AddChoiceCommand.Execute(null);
        }
    }

    public bool AllowPartialCredit
    {
        get => _question.AllowPartialCredit;
        set
        {
            if (_question.AllowPartialCredit == value) return;
            _question.AllowPartialCredit = value;
            OnPropertyChanged();
            MarkChanged();
        }
    }

    public override IReadOnlyList<string> ValidationMessages
    {
        get
        {
            var messages = new List<string>(base.ValidationMessages);

            // Legal, but almost certainly a mistake: a "select all that apply"
            // with one answer is a single-answer question wearing a disguise.
            if (Choices.Count(c => c.IsCorrect) == 1)
                messages.Add("Only one option is marked correct. Consider the single-answer type instead.");

            return messages;
        }
    }
}
