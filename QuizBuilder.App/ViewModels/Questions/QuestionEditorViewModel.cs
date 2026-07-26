using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.App.ViewModels.Questions;

/// <summary>
/// Base for the seven per-type editors.
///
/// The DataTemplate selection in QuizBuilderView maps each concrete subclass to
/// its editor View. That is the opposite of the shell's choice (views resolved
/// from DI) and deliberately so: the shell has seven fixed views resolved once
/// at startup, where a silent DataContext failure would leave a blank tab
/// forever. These editors are created per question as the user clicks around,
/// and DI cannot resolve "an editor for THIS question object" without a factory
/// per type. DataTemplate maps ViewModel -> View, which is exactly this job, and
/// a missing template renders the type name -- loud enough to spot immediately.
///
/// Every setter routes edits through IQuizDocumentService so the document's
/// dirty flag and change notifications stay correct. Mutating the model
/// directly would leave the title bar claiming the file is saved.
/// </summary>
public abstract class QuestionEditorViewModel : ViewModelBase
{
    private readonly Action _notifyChanged;
    private readonly IQuizPackageService _images;

    protected QuestionEditorViewModel(Question question, Action notifyChanged, IQuizPackageService images)
    {
        Model = question ?? throw new ArgumentNullException(nameof(question));
        _notifyChanged = notifyChanged ?? throw new ArgumentNullException(nameof(notifyChanged));
        _images = images ?? throw new ArgumentNullException(nameof(images));

        RemoveImageCommand = new RelayCommand(RemoveImage, () => HasImage);
    }

    public Question Model { get; }

    /// <summary>
    /// Called before a structural edit made from inside the editor, so it can
    /// be undone. Attaching or removing an image is a discrete action, unlike
    /// typing, which the text box undoes itself.
    /// <para>
    /// A settable hook rather than a constructor dependency: the seven editor
    /// subclasses would all need threading through otherwise, and every one of
    /// them would have to be updated again for the next such change. Null in
    /// tests, which construct editors directly and have nothing to undo into.
    /// </para>
    /// </summary>
    public Action<string>? CaptureBeforeChange { get; set; }

    // --- Image ---------------------------------------------------------------

    public RelayCommand RemoveImageCommand { get; }

    public bool HasImage => !string.IsNullOrEmpty(Model.ImageRelativePath);

    /// <summary>
    /// The image bytes for display, or null. The View turns these into an
    /// ImageSource with a converter; the VM stays free of WPF imaging types so
    /// it remains testable.
    /// </summary>
    public byte[]? ImageBytes =>
        string.IsNullOrEmpty(Model.ImageRelativePath) ? null : _images.GetImage(Model.ImageRelativePath);

    /// <summary>
    /// Attaches an image from raw bytes (the View reads the file in a dialog and
    /// passes them here). Content-addressed storage dedupes, so the same picture
    /// on several questions is stored once.
    /// </summary>
    public void AttachImage(byte[] bytes, string fileName)
    {
        // Before the mutation: the snapshot is the state undo returns to.
        CaptureBeforeChange?.Invoke("Add image");

        Model.ImageRelativePath = _images.AddImage(bytes, fileName);

        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(ImageBytes));
        RelayCommand.RaiseCanExecuteChanged();
        MarkChanged();
    }

    private void RemoveImage()
    {
        CaptureBeforeChange?.Invoke("Remove image");

        // Only the reference is cleared. The bytes stay in the package's working
        // set; the next save prunes them if nothing else points at them.
        Model.ImageRelativePath = null;

        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(ImageBytes));
        RelayCommand.RaiseCanExecuteChanged();
        MarkChanged();
    }

    public Guid Id => Model.Id;

    public string KindDisplayName => Model.KindDisplayName;

    public string Prompt
    {
        get => Model.Prompt;
        set
        {
            if (Model.Prompt == value) return;
            Model.Prompt = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PromptSummary));
            OnPropertyChanged(nameof(HasPrompt));
            OnPromptChanged();
            MarkChanged();
        }
    }

    /// <summary>Overridden by fill-in-the-blank to resync its tokens.</summary>
    protected virtual void OnPromptChanged() { }

    public bool HasPrompt => !string.IsNullOrWhiteSpace(Model.Prompt);

    /// <summary>
    /// One-line label for the question list. An empty prompt shows a
    /// placeholder rather than a blank row, which would look like a rendering
    /// fault rather than an unfinished question.
    /// </summary>
    public string PromptSummary
    {
        get
        {
            var text = Model.Prompt?.Trim();
            if (string.IsNullOrEmpty(text)) return "(no question text yet)";

            var singleLine = text.ReplaceLineEndings(" ");
            return singleLine.Length <= 80 ? singleLine : singleLine[..77] + "...";
        }
    }

    public double Points
    {
        get => Model.Points;
        set
        {
            // Clamp: a negative value would mean answering correctly loses
            // marks, which is never what someone meant to type.
            var clamped = Math.Clamp(value, 0, 1000);
            if (Math.Abs(Model.Points - clamped) < 0.001) return;

            Model.Points = clamped;
            OnPropertyChanged();
            MarkChanged();
        }
    }

    public string? Hint
    {
        get => Model.Hint;
        set
        {
            // Normalise empty to null so the JSON stays clean and "has a hint"
            // is a single check rather than two.
            var normalised = string.IsNullOrWhiteSpace(value) ? null : value;
            if (Model.Hint == normalised) return;

            Model.Hint = normalised;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasHint));
            MarkChanged();
        }
    }

    public bool HasHint => !string.IsNullOrWhiteSpace(Model.Hint);

    /// <summary>
    /// Problems with this question, shown inline. Empty means valid. This is
    /// advisory, never blocking: someone mid-thought should not be stopped from
    /// switching questions because a prompt is half-typed.
    /// </summary>
    public virtual IReadOnlyList<string> ValidationMessages
    {
        get
        {
            var messages = new List<string>();
            if (!HasPrompt) messages.Add("No question text yet.");
            return messages;
        }
    }

    public bool HasValidationMessages => ValidationMessages.Count > 0;

    protected void MarkChanged()
    {
        _notifyChanged();
        OnPropertyChanged(nameof(ValidationMessages));
        OnPropertyChanged(nameof(HasValidationMessages));
    }

    /// <summary>Builds the right editor for a question. One place to extend.</summary>
    public static QuestionEditorViewModel For(Question question, Action notifyChanged, IQuizPackageService images) => question switch
    {
        MultipleChoiceSingleQuestion q => new MultipleChoiceSingleEditorViewModel(q, notifyChanged, images),
        MultipleChoiceMultipleQuestion q => new MultipleChoiceMultipleEditorViewModel(q, notifyChanged, images),
        TrueFalseQuestion q => new TrueFalseEditorViewModel(q, notifyChanged, images),
        ShortAnswerQuestion q => new ShortAnswerEditorViewModel(q, notifyChanged, images),
        FillInTheBlankQuestion q => new FillInTheBlankEditorViewModel(q, notifyChanged, images),
        MatchingQuestion q => new MatchingEditorViewModel(q, notifyChanged, images),
        SequenceQuestion q => new SequenceEditorViewModel(q, notifyChanged, images),
        EssayQuestion q => new EssayEditorViewModel(q, notifyChanged, images),

        // Not a silent fallback: a new question type added to Core without an
        // editor here should fail loudly in development, not render blank.
        _ => throw new NotSupportedException(
            $"No editor for question type '{question.GetType().Name}'.")
    };
}
