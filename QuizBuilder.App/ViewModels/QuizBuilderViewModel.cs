using System.Collections.ObjectModel;
using System.IO;
using QuizBuilder.App.ViewModels.Questions;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.App.ViewModels;

/// <summary>A section in the left-hand list.</summary>
/// <summary>
/// What the user is about to lose by deleting a section. Passed to the view so
/// the dialog can name it: "Delete 'Chapter 3'? This also deletes its 12
/// questions." A count alone is not enough -- the title is what tells the user
/// whether they selected the section they meant.
/// </summary>
public sealed record SectionDeleteRequest(string Title, int QuestionCount);

public sealed class SectionViewModel : ViewModelBase
{
    private readonly Action<Guid, string> _rename;
    private bool _isSelected;

    public SectionViewModel(Section model, Action<Guid, string> rename)
    {
        Model = model;
        _rename = rename;
    }

    public Section Model { get; }

    public Guid Id => Model.Id;

    /// <summary>
    /// Routes through IQuizDocumentService.RenameSection rather than assigning
    /// Model.Title directly: the service owns the blank -> "Untitled Section"
    /// rule and raises SectionRenamed. Setting the model here would apply a
    /// second, silently different rule and raise the wrong event.
    ///
    /// Bound with UpdateSourceTrigger=LostFocus. With PropertyChanged, clearing
    /// the box to retype would hit the coercion on the empty string and reload
    /// "Untitled Section" under the caret.
    /// </summary>
    public string Title
    {
        get => Model.Title;
        set
        {
            if (Model.Title == value) return;

            _rename(Model.Id, value);

            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    /// <summary>An untitled section shows a placeholder rather than a blank row.</summary>
    public string DisplayTitle =>
        string.IsNullOrWhiteSpace(Model.Title) ? "(untitled section)" : Model.Title;

    public int QuestionCount => Model.Questions.Count;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void RefreshCount() => OnPropertyChanged(nameof(QuestionCount));
}

/// <summary>A question row in the middle list.</summary>
public sealed class QuestionRowViewModel : ViewModelBase
{
    private bool _isSelected;

    public QuestionRowViewModel(QuestionEditorViewModel editor) => Editor = editor;

    public QuestionEditorViewModel Editor { get; }

    public Guid Id => Editor.Id;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Editor));
    }
}

/// <summary>An entry in the "add question" menu.</summary>
public sealed record QuestionTypeOption(QuestionKind Kind, string Label, string Description);

/// <summary>
/// The Quiz Builder tab.
///
/// Owns the section list, the question list for the selected section, and the
/// editor for the selected question. It does not own theming, settings or
/// export: shared state lives in IQuizDocumentService, which every tab reads.
/// </summary>
public sealed class QuizBuilderViewModel : ViewModelBase
{
    private readonly IQuizDocumentService _document;
    private readonly IQuizPackageService _package;
    private readonly ISettingsService _settings;
    private readonly IAutoSaveService _autoSave;
    private readonly IQuestionBankService _bank;
    private readonly IUndoService _undo;

    private SectionViewModel? _selectedSection;
    private QuestionRowViewModel? _selectedQuestion;
    private string _statusMessage = string.Empty;

    public QuizBuilderViewModel(
        IQuizDocumentService document,
        IQuizPackageService package,
        ISettingsService settings,
        IAutoSaveService autoSave,
        IQuestionBankService bank,
        IUndoService undo)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _package = package ?? throw new ArgumentNullException(nameof(package));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _autoSave = autoSave ?? throw new ArgumentNullException(nameof(autoSave));
        _bank = bank ?? throw new ArgumentNullException(nameof(bank));
        _undo = undo ?? throw new ArgumentNullException(nameof(undo));

        Sections = new ObservableCollection<SectionViewModel>();
        Questions = new ObservableCollection<QuestionRowViewModel>();

        QuestionTypes = new[]
        {
            new QuestionTypeOption(QuestionKind.MultipleChoiceSingle, "Multiple choice", "One correct answer"),
            new QuestionTypeOption(QuestionKind.MultipleChoiceMultiple, "Multiple choice (several)", "Select all that apply"),
            new QuestionTypeOption(QuestionKind.TrueFalse, "True / False", "A single statement"),
            new QuestionTypeOption(QuestionKind.ShortAnswer, "Short answer", "A word or phrase, typed"),
            new QuestionTypeOption(QuestionKind.FillInTheBlank, "Fill in the blank", "Gaps within a sentence"),
            new QuestionTypeOption(QuestionKind.Matching, "Matching", "Pair items across two columns"),
            new QuestionTypeOption(QuestionKind.Sequence, "Sequence", "Put items in the correct order"),
            new QuestionTypeOption(QuestionKind.Essay, "Essay", "A long written response, graded by hand"),
        };

        AddSectionCommand = new RelayCommand(AddSection);
        RemoveSectionCommand = new RelayCommand(RemoveSection, () => SelectedSection is not null);

        // Braced so the lambdas are Action, not Func<bool>: Undo/Redo return a
        // success flag that RelayCommand has no parameter to receive.
        UndoCommand = new RelayCommand(() => { _undo.Undo(); }, () => _undo.CanUndo);
        RedoCommand = new RelayCommand(() => { _undo.Redo(); }, () => _undo.CanRedo);

        // The commands' CanExecute reads the undo service, so the UI must be
        // told to re-query when the stacks change or the buttons stay stale.
        _undo.StateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(UndoLabel));
            OnPropertyChanged(nameof(RedoLabel));
            RelayCommand.RaiseCanExecuteChanged();
        };
        MoveSectionUpCommand = new RelayCommand(() => MoveSection(-1), () => CanMoveSection(-1));
        MoveSectionDownCommand = new RelayCommand(() => MoveSection(1), () => CanMoveSection(1));

        AddQuestionCommand = new RelayCommand(p =>
        {
            if (p is QuestionTypeOption option) AddQuestion(option.Kind);
        });

        RemoveQuestionCommand = new RelayCommand(RemoveQuestion, () => SelectedQuestion is not null);
        DuplicateQuestionCommand = new RelayCommand(DuplicateQuestion, () => SelectedQuestion is not null);
        SaveToBankCommand = new RelayCommand(SaveToBank, () => SelectedQuestion is not null);
        MoveQuestionUpCommand = new RelayCommand(() => MoveQuestion(-1), () => CanMoveQuestion(-1));
        MoveQuestionDownCommand = new RelayCommand(() => MoveQuestion(1), () => CanMoveQuestion(1));

        NewCommand = new RelayCommand(NewQuiz);

        _document.DocumentChanged += (_, e) => OnDocumentChanged(e);

        Rebuild();
    }

    public ObservableCollection<SectionViewModel> Sections { get; }
    public ObservableCollection<QuestionRowViewModel> Questions { get; }
    public IReadOnlyList<QuestionTypeOption> QuestionTypes { get; }

    public RelayCommand AddSectionCommand { get; }
    public RelayCommand RemoveSectionCommand { get; }

    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }

    /// <summary>
    /// Names the change that would be reversed, e.g. "Undo Delete section",
    /// so the user knows what they are about to get back before they press it.
    /// </summary>
    public string UndoLabel =>
        _undo.NextUndoLabel is { } label ? $"Undo {label}" : "Undo";

    public string RedoLabel =>
        _undo.NextRedoLabel is { } label ? $"Redo {label}" : "Redo";
    public RelayCommand MoveSectionUpCommand { get; }
    public RelayCommand MoveSectionDownCommand { get; }
    public RelayCommand AddQuestionCommand { get; }
    public RelayCommand RemoveQuestionCommand { get; }
    public RelayCommand DuplicateQuestionCommand { get; }
    public RelayCommand SaveToBankCommand { get; }
    public RelayCommand MoveQuestionUpCommand { get; }
    public RelayCommand MoveQuestionDownCommand { get; }
    public RelayCommand NewCommand { get; }

    // --- Document-level -----------------------------------------------------

    public string QuizTitle
    {
        get => _document.Current.Title;
        set
        {
            if (_document.Current.Title == value) return;

            // SetTitle raises TitleChanged, which OnDocumentChanged deliberately
            // ignores. Notifying here (once) is enough, and avoids the TextBox
            // being re-read mid-keystroke.
            _document.SetTitle(value);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// SetDescription raises TitleChanged, which OnDocumentChanged ignores
    /// deliberately -- this setter has already notified. Safe with a
    /// PropertyChanged binding because nothing coerces the value back.
    /// </summary>
    public string QuizDescription
    {
        get => _document.Current.Description;
        set
        {
            if (_document.Current.Description == value) return;

            _document.SetDescription(value);
            OnPropertyChanged();
        }
    }

    public bool IsDirty => _document.IsDirty;

    public string? CurrentFilePath => _document.CurrentFilePath;

    public string FileDisplayName =>
        string.IsNullOrEmpty(_document.CurrentFilePath)
            ? "Not saved yet"
            : Path.GetFileName(_document.CurrentFilePath);

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    // --- Selection ----------------------------------------------------------

    public SectionViewModel? SelectedSection
    {
        get => _selectedSection;
        set
        {
            if (ReferenceEquals(_selectedSection, value)) return;

            if (_selectedSection is not null) _selectedSection.IsSelected = false;
            _selectedSection = value;
            if (_selectedSection is not null) _selectedSection.IsSelected = true;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedSection));

            RebuildQuestions();
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelectedSection => SelectedSection is not null;

    public QuestionRowViewModel? SelectedQuestion
    {
        get => _selectedQuestion;
        set
        {
            if (ReferenceEquals(_selectedQuestion, value)) return;

            if (_selectedQuestion is not null) _selectedQuestion.IsSelected = false;
            _selectedQuestion = value;
            if (_selectedQuestion is not null) _selectedQuestion.IsSelected = true;

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedQuestion));
            OnPropertyChanged(nameof(SelectedEditor));

            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasSelectedQuestion => SelectedQuestion is not null;

    /// <summary>The editor bound to the right-hand panel, via DataTemplate.</summary>
    public QuestionEditorViewModel? SelectedEditor => SelectedQuestion?.Editor;

    public bool HasSections => Sections.Count > 0;
    public bool HasQuestions => Questions.Count > 0;

    // --- Sections -----------------------------------------------------------

    private void AddSection()
    {
        // AddSection takes a title and builds the Section itself -- it also
        // maintains SectionDisplayOrder, which constructing one here would skip.
        _undo.CaptureBeforeChange("Add section");
        var section = _document.AddSection($"Section {Sections.Count + 1}");

        // Select what was just created: the user's next action is almost
        // certainly to name it or add a question to it.
        SelectedSection = Sections.FirstOrDefault(s => s.Id == section.Id);
        StatusMessage = "Section added.";
    }

    /// <summary>
    /// Asked before a section is deleted. Returns true to proceed.
    /// <para>
    /// A hook rather than a MessageBox call: this view model has no WPF
    /// dependency, and adding one to show a dialog would make every test that
    /// deletes a section need a message pump.
    /// </para>
    /// <para>
    /// Null means no confirmation, which is what tests want.
    /// </para>
    /// </summary>
    public Func<SectionDeleteRequest, bool>? ConfirmSectionDelete { get; set; }

    private void RemoveSection()
    {
        if (SelectedSection is null) return;

        var questionCount = SelectedSection.QuestionCount;

        // Only confirm when something would actually be lost. An empty section
        // destroys nothing, and prompting there trains the user to dismiss the
        // dialog that does matter.
        if (questionCount > 0 && ConfirmSectionDelete is not null)
        {
            var request = new SectionDeleteRequest(SelectedSection.Title, questionCount);
            if (!ConfirmSectionDelete(request)) return;
        }

        _undo.CaptureBeforeChange(questionCount > 0 ? "Delete section" : "Delete empty section");

        var index = Sections.IndexOf(SelectedSection);
        _document.RemoveSection(SelectedSection.Id);

        // Select a neighbour rather than clearing the selection: an empty
        // right-hand panel after a delete reads as "something broke".
        SelectedSection = Sections.Count == 0
            ? null
            : Sections[Math.Min(index, Sections.Count - 1)];

        StatusMessage = "Section removed.";
    }

    private bool CanMoveSection(int delta)
    {
        if (SelectedSection is null) return false;

        var index = Sections.IndexOf(SelectedSection);
        var target = index + delta;
        return target >= 0 && target < Sections.Count;
    }

    private void MoveSection(int delta)
    {
        if (SelectedSection is null || !CanMoveSection(delta)) return;

        var id = SelectedSection.Id;
        var target = Sections.IndexOf(SelectedSection) + delta;

        _undo.CaptureBeforeChange("Move section");
        _document.MoveSection(id, target);

        SelectedSection = Sections.FirstOrDefault(s => s.Id == id);
        StatusMessage = "Section moved.";
    }

    // --- Questions ----------------------------------------------------------

    private void AddQuestion(QuestionKind kind)
    {
        if (SelectedSection is null) return;

        var question = CreateQuestion(kind);
        question.Points = _settings.Current.Quiz.PointsFor(kind);

        _undo.CaptureBeforeChange("Add question");
        _document.AddQuestion(SelectedSection.Id, question);

        SelectedQuestion = Questions.FirstOrDefault(q => q.Id == question.Id);
        StatusMessage = $"{question.KindDisplayName} added.";
    }

    private static Question CreateQuestion(QuestionKind kind) => kind switch
    {
        QuestionKind.MultipleChoiceSingle => new MultipleChoiceSingleQuestion(),
        QuestionKind.MultipleChoiceMultiple => new MultipleChoiceMultipleQuestion(),
        QuestionKind.TrueFalse => new TrueFalseQuestion(),
        QuestionKind.ShortAnswer => new ShortAnswerQuestion(),
        QuestionKind.FillInTheBlank => new FillInTheBlankQuestion(),
        QuestionKind.Matching => new MatchingQuestion(),
        QuestionKind.Sequence => new SequenceQuestion(),
        QuestionKind.Essay => new EssayQuestion(),
        _ => throw new NotSupportedException($"Unknown question kind '{kind}'.")
    };

    private void RemoveQuestion()
    {
        if (SelectedSection is null || SelectedQuestion is null) return;

        var index = Questions.IndexOf(SelectedQuestion);
        _undo.CaptureBeforeChange("Delete question");
        _document.RemoveQuestion(SelectedSection.Id, SelectedQuestion.Id);

        SelectedQuestion = Questions.Count == 0
            ? null
            : Questions[Math.Min(index, Questions.Count - 1)];

        StatusMessage = "Question removed.";
    }

    private void DuplicateQuestion()
    {
        if (SelectedSection is null || SelectedQuestion is null) return;

        // Clone() assigns a new Id via CopyBaseTo, so the duplicate is a
        // genuinely separate question rather than an alias.
        var copy = SelectedQuestion.Editor.Model.Clone();
        _undo.CaptureBeforeChange("Duplicate question");
        _document.AddQuestion(SelectedSection.Id, copy);

        SelectedQuestion = Questions.FirstOrDefault(q => q.Id == copy.Id);
        StatusMessage = "Question duplicated.";
    }

    private void SaveToBank()
    {
        if (SelectedQuestion is null) return;

        // The bank stores its own clone (image-free), so this is a copy-out: the
        // question stays in the quiz and an independent copy joins the bank. No
        // category here -- the author sets one later on the Question Bank tab.
        _bank.Add(SelectedQuestion.Editor.Model, category: null);

        StatusMessage = "Saved to question bank.";
    }

    private bool CanMoveQuestion(int delta)
    {
        if (SelectedQuestion is null) return false;

        var index = Questions.IndexOf(SelectedQuestion);
        var target = index + delta;
        return target >= 0 && target < Questions.Count;
    }

    /// <summary>
    /// Alt+Up / Alt+Down. The keyboard path to reordering: drag-and-drop alone
    /// would exclude anyone not using a mouse, and this is also faster for
    /// moving one row.
    /// </summary>
    private void MoveQuestion(int delta)
    {
        if (SelectedSection is null || SelectedQuestion is null) return;
        if (!CanMoveQuestion(delta)) return;   // at a boundary: no-op, never wrap

        var id = SelectedQuestion.Id;
        var target = Questions.IndexOf(SelectedQuestion) + delta;

        _undo.CaptureBeforeChange("Move question");
        _document.MoveQuestion(SelectedSection.Id, id, SelectedSection.Id, target);

        SelectedQuestion = Questions.FirstOrDefault(q => q.Id == id);
        StatusMessage = "Question moved.";
    }

    /// <summary>Drag-and-drop reordering of sections, by dropping onto a target.</summary>
    public void MoveSectionTo(Guid sectionId, int newIndex)
    {
        _undo.CaptureBeforeChange("Move section");
        _document.MoveSection(sectionId, newIndex);

        // Keep the moved section selected so the editor stays put.
        SelectedSection = Sections.FirstOrDefault(s => s.Id == sectionId);
        StatusMessage = "Section moved.";
    }

    /// <summary>Drag-and-drop, including across sections.</summary>
    public void MoveQuestionTo(Guid questionId, Guid fromSectionId, Guid toSectionId, int newIndex)
    {
        _undo.CaptureBeforeChange("Move question");
        _document.MoveQuestion(fromSectionId, questionId, toSectionId, newIndex);

        if (SelectedSection?.Id == toSectionId)
            SelectedQuestion = Questions.FirstOrDefault(q => q.Id == questionId);

        StatusMessage = "Question moved.";
    }

    // --- File ---------------------------------------------------------------

    private void NewQuiz()
    {
        // NewDocument raises DocumentReplaced -> OnDocumentChanged -> Rebuild().
        _document.NewDocument();
        StatusMessage = "New quiz started.";
    }

    /// <summary>
    /// Saves to an explicit path. The view supplies it from a file dialog:
    /// opening a dialog here would put a WPF dependency in the ViewModel and
    /// make this untestable.
    /// </summary>
    public async Task<bool> SaveToAsync(string path)
    {
        try
        {
            await _package.SaveAsync(_document.Current, path);
            _document.MarkSaved(path);

            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(CurrentFilePath));
            OnPropertyChanged(nameof(FileDisplayName));

            // Autosave has been dormant with nowhere to write. Now there is a
            // path, so start the timer.
            _autoSave.Reconfigure();

            StatusMessage = $"Saved to {Path.GetFileName(path)}.";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not save: {ex.Message}";
            return false;
        }
    }

    public async Task<bool> OpenAsync(string path)
    {
        try
        {
            var result = await _package.LoadAsync(path);

            // LoadDocument raises DocumentReplaced, which OnDocumentChanged
            // already answers with a full Rebuild(). Calling Rebuild() here too
            // would just do it twice.
            _document.LoadDocument(result.Document, path);

            _autoSave.Reconfigure();

            StatusMessage = $"Opened {Path.GetFileName(path)}.";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open: {ex.Message}";
            return false;
        }
    }

    // --- Rebuild ------------------------------------------------------------

    /// <summary>
    /// Reacts to document changes BY KIND.
    ///
    /// Rebuilding on every change looks safer and is in fact the bug: three of
    /// these kinds are raised by this ViewModel's own setters (TitleChanged by
    /// QuizTitle, SectionRenamed by SectionViewModel.Title, QuestionChanged by
    /// the editors). Those setters have already raised OnPropertyChanged.
    /// Reacting again tears down and recreates the very object the user is
    /// typing into: the caret resets, the ListBox selection clears, and the
    /// editor panel blanks mid-edit.
    ///
    /// So: only rebuild for changes that alter list MEMBERSHIP or ORDER, which
    /// are the ones this ViewModel did not cause by editing a bound property.
    /// </summary>
    private void OnDocumentChanged(DocumentChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsDirty));

        switch (e.Kind)
        {
            case DocumentChangeKind.DocumentReplaced:
                // New or Open: nothing on screen survives, so rebuild it all.
                Rebuild();
                break;

            case DocumentChangeKind.SectionAdded:
            case DocumentChangeKind.SectionRemoved:
            case DocumentChangeKind.SectionsReordered:
                SyncSections();
                RebuildQuestions();
                break;

            case DocumentChangeKind.QuestionAdded:
            case DocumentChangeKind.QuestionRemoved:
            case DocumentChangeKind.QuestionsReordered:
                RebuildQuestions();
                RefreshSectionCounts();
                break;

            case DocumentChangeKind.TitleChanged:
            case DocumentChangeKind.SectionRenamed:
            case DocumentChangeKind.QuestionChanged:
                // Self-inflicted: the setter that raised this already notified.
                // Touching the lists here is what broke editing.
                break;

            default:
                // ThemeChanged and anything added later: the lists are unaffected.
                break;
        }
    }

    private void RefreshSectionCounts()
    {
        foreach (var section in Sections) section.RefreshCount();
    }

    private void Rebuild()
    {
        // Clear the selection BEFORE syncing. Otherwise SyncSections restores
        // the old selection by id, and the assignment below then finds the
        // same object, hits the ReferenceEquals guard in the setter, and
        // returns without ever calling RebuildQuestions -- so the section
        // looks selected while its question list stays empty. Nothing throws;
        // the questions are in the document, just never shown.
        _selectedSection = null;
        _selectedQuestion = null;

        SyncSections();

        SelectedSection = Sections.FirstOrDefault();

        // Rebuild the question list unconditionally rather than relying on the
        // SelectedSection setter to do it. When the document has no sections,
        // the assignment above is null-to-null, the setter's ReferenceEquals
        // guard returns early, and RebuildQuestions never runs -- leaving the
        // previous document's questions listed under no section at all. That
        // is reachable through undo/redo, which route through DocumentReplaced
        // rather than the SectionRemoved path that rebuilds explicitly.
        RebuildQuestions();

        OnPropertyChanged(nameof(QuizTitle));
        OnPropertyChanged(nameof(QuizDescription));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CurrentFilePath));
        OnPropertyChanged(nameof(FileDisplayName));
    }

    private void SyncSections()
    {
        var selectedId = SelectedSection?.Id;

        Sections.Clear();
        foreach (var section in _document.Current.Sections)
            Sections.Add(new SectionViewModel(section, RenameSection));

        OnPropertyChanged(nameof(HasSections));

        // Reconcile unconditionally, for the same reason as RebuildQuestions:
        // guarding on "was something selected" means callers that null the
        // field before calling here skip the notifications entirely. Nothing
        // visible depends on it today -- there is no section detail panel the
        // way there is a question editor -- but the shape is the trap, so it
        // does not get to survive here either.
        //
        // Assign the FIELD, not the property: the list was just rebuilt, so a
        // restored SectionViewModel is a new object wrapping the same Section.
        // Going through the setter would fire RebuildQuestions and discard the
        // live question editors for no reason. Because the field skips the
        // setter, callers refresh the question list explicitly.
        var restored = selectedId is null
            ? null
            : Sections.FirstOrDefault(s => s.Id == selectedId);

        _selectedSection = restored;
        if (restored is not null) restored.IsSelected = true;

        OnPropertyChanged(nameof(SelectedSection));
        OnPropertyChanged(nameof(HasSelectedSection));

        RelayCommand.RaiseCanExecuteChanged();
    }

    private void RebuildQuestions()
    {
        var selectedId = SelectedQuestion?.Id;

        Questions.Clear();

        if (SelectedSection is not null)
        {
            foreach (var question in SelectedSection.Model.Questions)
            {
                var editor = QuestionEditorViewModel.For(question, MarkDocumentChanged, _package);

                // Image attach/remove is structural, so it goes on the undo
                // stack. Typing in the editor deliberately does not: the text
                // box has its own undo, and mixing the two scopes makes Ctrl+Z
                // unpredictable.
                editor.CaptureBeforeChange = _undo.CaptureBeforeChange;

                Questions.Add(new QuestionRowViewModel(editor));
            }
        }

        OnPropertyChanged(nameof(HasQuestions));

        // Reconcile the selection unconditionally.
        //
        // Guarding this on "was something selected" reads as an optimisation
        // but leaves the editor panel stale: Rebuild() nulls _selectedQuestion
        // before calling here, so selectedId is already null on that path, the
        // block is skipped, and SelectedEditor/HasSelectedQuestion are never
        // re-raised. The field is null but the panel still renders the last
        // question's editor, under a section that no longer exists.
        //
        // Restoring by id where possible still matters -- rebuilding a list
        // under the user and silently dropping their place loses it.
        var restored = selectedId is null
            ? null
            : Questions.FirstOrDefault(q => q.Id == selectedId);

        _selectedQuestion = restored;
        if (restored is not null) restored.IsSelected = true;

        OnPropertyChanged(nameof(SelectedQuestion));
        OnPropertyChanged(nameof(HasSelectedQuestion));
        OnPropertyChanged(nameof(SelectedEditor));

        foreach (var section in Sections) section.RefreshCount();

        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Renames via the service so the coercion rule and the raised event stay
    /// in one place. SectionRenamed is deliberately ignored by
    /// OnDocumentChanged -- the setter above has already notified.
    /// </summary>
    private void RenameSection(Guid sectionId, string title)
    {
        _undo.CaptureBeforeChange("Rename section");
        _document.RenameSection(sectionId, title);
        OnPropertyChanged(nameof(IsDirty));
    }

    /// <summary>
    /// Called by the editors when they mutate their model. The document service
    /// owns the dirty flag, so it must be told rather than the flag being set
    /// here.
    /// </summary>
    private void MarkDocumentChanged()
    {
        if (SelectedSection is null || SelectedQuestion is null)
        {
            OnPropertyChanged(nameof(IsDirty));
            return;
        }

        _document.NotifyQuestionChanged(SelectedSection.Id, SelectedQuestion.Id);

        OnPropertyChanged(nameof(IsDirty));
    }
}
