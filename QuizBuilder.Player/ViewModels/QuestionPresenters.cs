using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;

namespace QuizBuilder.Player.ViewModels;

/// <summary>
/// Base for a single question's interactive presentation. Each subclass owns the
/// widgets for one question type and writes the taker's input straight into the
/// shared <see cref="QuestionAnswer"/> that the grader will read. Keeping the
/// answer object as the single mutation target means the take VM never has to
/// know the per-type shape.
/// </summary>
public abstract partial class QuestionPresenter : ObservableObject
{
    protected QuestionPresenter(CompiledQuestion compiled, QuestionAnswer answer, byte[]? image)
    {
        Compiled = compiled;
        Answer = answer;
        ImageBytes = image;
    }

    public CompiledQuestion Compiled { get; }
    public QuestionAnswer Answer { get; }

    /// <summary>Decoded image bytes for this question, or null. Bound via a
    /// byte[]-to-ImageSource converter in the view.</summary>
    public byte[]? ImageBytes { get; }

    public bool HasImage => ImageBytes is { Length: > 0 };

    public string Prompt => Compiled.Question.Prompt;
    public int Number => Compiled.Number;
    public string KindLabel => Compiled.Question.KindDisplayName;
    public double Points => Compiled.Question.Points;

    public string PointsLabel => Points == 1 ? "1 point" : $"{Points:0.##} points";

    /// <summary>
    /// A one-element list containing this presenter, so a BindableLayout with a
    /// template selector can host it as a single templated item. (A plain
    /// ContentView cannot take a DataTemplateSelector.)
    /// </summary>
    public IReadOnlyList<QuestionPresenter> SelfList => new[] { this };

    /// <summary>Factory: builds the right presenter for a compiled question.</summary>
    public static QuestionPresenter Create(CompiledQuestion compiled, QuestionAnswer answer, byte[]? image)
        => compiled.Question switch
        {
            MultipleChoiceSingleQuestion => new SingleChoicePresenter(compiled, answer, image),
            MultipleChoiceMultipleQuestion => new MultiChoicePresenter(compiled, answer, image),
            TrueFalseQuestion => new TrueFalsePresenter(compiled, answer, image),
            ShortAnswerQuestion => new ShortAnswerPresenter(compiled, answer, image),
            FillInTheBlankQuestion => new FillBlankPresenter(compiled, answer, image),
            MatchingQuestion => new MatchingPresenter(compiled, answer, image),
            SequenceQuestion => new SequencePresenter(compiled, answer, image),
            NumericQuestion => new NumericPresenter(compiled, answer, image),
            DropdownQuestion => new DropdownPresenter(compiled, answer, image),
            EssayQuestion => new EssayPresenter(compiled, answer, image),
            _ => new UnsupportedPresenter(compiled, answer, image),
        };
}

// ---------------------------------------------------------------------------
// Multiple choice (single answer)
// ---------------------------------------------------------------------------
public sealed partial class SingleChoicePresenter : QuestionPresenter
{
    public SingleChoicePresenter(CompiledQuestion c, QuestionAnswer a, byte[]? img) : base(c, a, img)
    {
        var q = (MultipleChoiceSingleQuestion)c.Question;
        Options = new ObservableCollection<ChoiceOption>(
            q.Choices.Select((choice, i) => new ChoiceOption(choice.Text, i, this)));

        // Reflect any prior selection (revisiting the question).
        if (a.ChoiceIndex is { } sel && sel >= 0 && sel < Options.Count)
            Options[sel].IsSelected = true;
    }

    public ObservableCollection<ChoiceOption> Options { get; }

    internal void Select(int index)
    {
        foreach (var o in Options) o.IsSelected = o.Index == index;
        Answer.ChoiceIndex = index;
    }
}

public sealed partial class ChoiceOption : ObservableObject
{
    private readonly SingleChoicePresenter? _single;
    private readonly MultiChoicePresenter? _multi;

    public ChoiceOption(string text, int index, SingleChoicePresenter parent)
    {
        Text = text; Index = index; _single = parent;
    }

    public ChoiceOption(string text, int index, MultiChoicePresenter parent)
    {
        Text = text; Index = index; _multi = parent;
    }

    public string Text { get; }
    public int Index { get; }

    [ObservableProperty] private bool _isSelected;

    [RelayCommand]
    private void Toggle()
    {
        if (_single is not null) _single.Select(Index);
        else _multi?.Toggle(Index);
    }
}

// ---------------------------------------------------------------------------
// Multiple choice (multiple answers)
// ---------------------------------------------------------------------------
public sealed partial class MultiChoicePresenter : QuestionPresenter
{
    public MultiChoicePresenter(CompiledQuestion c, QuestionAnswer a, byte[]? img) : base(c, a, img)
    {
        var q = (MultipleChoiceMultipleQuestion)c.Question;
        Options = new ObservableCollection<ChoiceOption>(
            q.Choices.Select((choice, i) => new ChoiceOption(choice.Text, i, this)));

        foreach (var i in a.ChoiceIndices)
            if (i >= 0 && i < Options.Count) Options[i].IsSelected = true;
    }

    public ObservableCollection<ChoiceOption> Options { get; }

    internal void Toggle(int index)
    {
        var opt = Options[index];
        opt.IsSelected = !opt.IsSelected;

        if (opt.IsSelected) Answer.ChoiceIndices.Add(index);
        else Answer.ChoiceIndices.Remove(index);
    }
}

// ---------------------------------------------------------------------------
// True / False
// ---------------------------------------------------------------------------
public sealed partial class TrueFalsePresenter : QuestionPresenter
{
    public TrueFalsePresenter(CompiledQuestion c, QuestionAnswer a, byte[]? img) : base(c, a, img)
    {
        _isTrueSelected = a.BoolAnswer == true;
        _isFalseSelected = a.BoolAnswer == false;
    }

    [ObservableProperty] private bool _isTrueSelected;
    [ObservableProperty] private bool _isFalseSelected;

    [RelayCommand]
    private void ChooseTrue()
    {
        IsTrueSelected = true; IsFalseSelected = false;
        Answer.BoolAnswer = true;
    }

    [RelayCommand]
    private void ChooseFalse()
    {
        IsTrueSelected = false; IsFalseSelected = true;
        Answer.BoolAnswer = false;
    }
}

// ---------------------------------------------------------------------------
// Short answer
// ---------------------------------------------------------------------------
public sealed partial class ShortAnswerPresenter : QuestionPresenter
{
    public ShortAnswerPresenter(CompiledQuestion c, QuestionAnswer a, byte[]? img) : base(c, a, img)
    {
        _text = a.TextAnswer ?? string.Empty;
    }

    [ObservableProperty] private string _text;

    partial void OnTextChanged(string value) => Answer.TextAnswer = value;
}

// ---------------------------------------------------------------------------
// Numeric (typed number, graded by tolerance in Core)
// ---------------------------------------------------------------------------

public sealed partial class NumericPresenter : QuestionPresenter
{
    public NumericPresenter(CompiledQuestion c, QuestionAnswer a, byte[]? img) : base(c, a, img)
    {
        var q = (NumericQuestion)c.Question;
        Unit = q.Unit ?? string.Empty;
        _text = a.TextAnswer ?? string.Empty;
    }

    /// <summary>Optional unit label shown beside the entry (may be empty).</summary>
    public string Unit { get; }

    public bool HasUnit => !string.IsNullOrWhiteSpace(Unit);

    // The taker types the number as text; Core parses and grades it (invariant
    // culture, tolerance). Storing it in TextAnswer keeps the answer model the
    // same as short-answer, so grading is exactly the desktop path.
    [ObservableProperty] private string _text;

    partial void OnTextChanged(string value) => Answer.TextAnswer = value;
}

// ---------------------------------------------------------------------------
// Dropdown (single choice presented as a picker)
// ---------------------------------------------------------------------------

public sealed partial class DropdownPresenter : QuestionPresenter
{
    public DropdownPresenter(CompiledQuestion c, QuestionAnswer a, byte[]? img) : base(c, a, img)
    {
        var q = (DropdownQuestion)c.Question;
        Options = new ObservableCollection<string>(q.Choices.Select(choice => choice.Text));

        // Reflect any prior selection (revisiting the question).
        if (a.ChoiceIndex is { } sel && sel >= 0 && sel < Options.Count)
            _selectedIndex = sel;
    }

    public ObservableCollection<string> Options { get; }

    // Picker.SelectedIndex binds here two-way. -1 means nothing chosen. We write
    // ChoiceIndex (the same answer field single-choice uses), so grading matches
    // the desktop single-choice path exactly.
    [ObservableProperty] private int _selectedIndex = -1;

    partial void OnSelectedIndexChanged(int value)
        => Answer.ChoiceIndex = value >= 0 ? value : null;
}
public sealed partial class FillBlankPresenter : QuestionPresenter
{
    public FillBlankPresenter(CompiledQuestion c, QuestionAnswer a, byte[]? img) : base(c, a, img)
    {
        var q = (FillInTheBlankQuestion)c.Question;
        var ordered = q.Blanks.OrderBy(b => b.Ordinal).ToList();

        Blanks = new ObservableCollection<BlankField>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var existing = a.BlankAnswers.TryGetValue(i, out var v) ? v : string.Empty;
            Blanks.Add(new BlankField(i, ordered[i].Ordinal, existing, a));
        }
    }

    public ObservableCollection<BlankField> Blanks { get; }
}

public sealed partial class BlankField : ObservableObject
{
    private readonly QuestionAnswer _answer;

    public BlankField(int index, int ordinal, string initial, QuestionAnswer answer)
    {
        Index = index; Ordinal = ordinal; _text = initial; _answer = answer;
        if (!string.IsNullOrEmpty(initial)) _answer.BlankAnswers[index] = initial;
    }

    public int Index { get; }
    public int Ordinal { get; }
    public string Label => $"Blank {Ordinal}";

    [ObservableProperty] private string _text;

    partial void OnTextChanged(string value) => _answer.BlankAnswers[Index] = value;
}

// ---------------------------------------------------------------------------
// Matching
// ---------------------------------------------------------------------------
public sealed partial class MatchingPresenter : QuestionPresenter
{
    public MatchingPresenter(CompiledQuestion c, QuestionAnswer a, byte[]? img) : base(c, a, img)
    {
        var q = (MatchingQuestion)c.Question;

        // The right-hand options the taker picks from: the compiled, shuffled
        // set including distractors. The grader compares the chosen STRING to
        // each pair's Right, so the picker offers strings, not indices.
        var options = (c.MatchingOptions ?? q.Pairs.Select(p => p.Right).ToList()).ToList();
        Options = new ObservableCollection<string>(options);

        Rows = new ObservableCollection<MatchRow>();
        for (var i = 0; i < q.Pairs.Count; i++)
        {
            var chosen = a.MatchAnswers.TryGetValue(i, out var v) ? v : null;
            Rows.Add(new MatchRow(i, q.Pairs[i].Left, options, chosen, a));
        }
    }

    public ObservableCollection<MatchRow> Rows { get; }
    public ObservableCollection<string> Options { get; }
}

public sealed partial class MatchRow : ObservableObject
{
    private readonly QuestionAnswer _answer;

    public MatchRow(int index, string left, IReadOnlyList<string> options, string? chosen, QuestionAnswer answer)
    {
        Index = index; Left = left; _answer = answer;
        Options = new ObservableCollection<string>(options);
        _selected = chosen;
        if (!string.IsNullOrEmpty(chosen)) _answer.MatchAnswers[index] = chosen!;
    }

    public int Index { get; }
    public string Left { get; }
    public ObservableCollection<string> Options { get; }

    [ObservableProperty] private string? _selected;

    partial void OnSelectedChanged(string? value)
    {
        if (string.IsNullOrEmpty(value)) _answer.MatchAnswers.Remove(Index);
        else _answer.MatchAnswers[Index] = value;
    }
}

// ---------------------------------------------------------------------------
// Sequence
// ---------------------------------------------------------------------------
public sealed partial class SequencePresenter : QuestionPresenter
{
    public SequencePresenter(CompiledQuestion c, QuestionAnswer a, byte[]? img) : base(c, a, img)
    {
        var q = (SequenceQuestion)c.Question;

        // The answer's SequenceAnswer was seeded with the presentation order at
        // take start. Build draggable items from it: each item carries the
        // AUTHORED index, and the visible order is the current answer order.
        Items = new ObservableCollection<SequenceItem>();
        foreach (var authoredIndex in a.SequenceAnswer)
        {
            if (authoredIndex >= 0 && authoredIndex < q.Items.Count)
                Items.Add(new SequenceItem(authoredIndex, q.Items[authoredIndex], this));
        }
    }

    public ObservableCollection<SequenceItem> Items { get; }

    /// <summary>Moves an item up or down and rewrites the answer's index order.</summary>
    internal void Move(SequenceItem item, int delta)
    {
        var from = Items.IndexOf(item);
        if (from < 0) return;
        var to = from + delta;
        if (to < 0 || to >= Items.Count) return;

        Items.Move(from, to);
        SyncAnswer();
    }

    private void SyncAnswer()
    {
        Answer.SequenceAnswer.Clear();
        Answer.SequenceAnswer.AddRange(Items.Select(i => i.AuthoredIndex));
    }
}

public sealed partial class SequenceItem : ObservableObject
{
    private readonly SequencePresenter _parent;

    public SequenceItem(int authoredIndex, string text, SequencePresenter parent)
    {
        AuthoredIndex = authoredIndex;
        Text = text;
        _parent = parent;
    }

    /// <summary>The item's index in the authored (correct) order -- what the
    /// grader scores against. NOT its visible position.</summary>
    public int AuthoredIndex { get; }
    public string Text { get; }

    // Commands live on the item so a BindableLayout item template can bind them
    // directly, without a RelativeSource walk to the presenter (BindableLayout
    // items don't have the presenter as a visual ancestor).
    [RelayCommand]
    private void MoveUp() => _parent.Move(this, -1);

    [RelayCommand]
    private void MoveDown() => _parent.Move(this, +1);
}

// ---------------------------------------------------------------------------
// Essay (captured, never auto-graded)
// ---------------------------------------------------------------------------
public sealed partial class EssayPresenter : QuestionPresenter
{
    public EssayPresenter(CompiledQuestion c, QuestionAnswer a, byte[]? img) : base(c, a, img)
    {
        _text = a.EssayAnswer ?? string.Empty;
        var q = (EssayQuestion)c.Question;
        SuggestedWordCount = q.SuggestedWordCount;
    }

    public int SuggestedWordCount { get; }
    public bool HasSuggestedWordCount => SuggestedWordCount > 0;
    public string SuggestedWordLabel => $"Suggested length: about {SuggestedWordCount} words";

    [ObservableProperty] private string _text;

    partial void OnTextChanged(string value) => Answer.EssayAnswer = value;
}

// ---------------------------------------------------------------------------
// Fallback (should never appear; keeps the switch total)
// ---------------------------------------------------------------------------
public sealed class UnsupportedPresenter : QuestionPresenter
{
    public UnsupportedPresenter(CompiledQuestion c, QuestionAnswer a, byte[]? img) : base(c, a, img) { }
}
