using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Interfaces;

public enum DocumentChangeKind
{
    /// <summary>A different document was loaded, or a new one started.</summary>
    DocumentReplaced,
    TitleChanged,
    SectionAdded,
    SectionRemoved,
    SectionRenamed,
    SectionsReordered,
    QuestionAdded,
    QuestionRemoved,
    QuestionChanged,
    QuestionsReordered,
    ThemeChanged,
    DisplayOrderChanged,

    /// <summary>A study card was added, edited, removed, or reordered.</summary>
    StudyCardsChanged
}

public sealed class DocumentChangedEventArgs : EventArgs
{
    public DocumentChangedEventArgs(DocumentChangeKind kind, Guid? sectionId = null, Guid? questionId = null)
    {
        Kind = kind;
        SectionId = sectionId;
        QuestionId = questionId;
    }

    public DocumentChangeKind Kind { get; }
    public Guid? SectionId { get; }
    public Guid? QuestionId { get; }
}

/// <summary>
/// Holds the single shared <see cref="QuizDocument"/>.
///
/// Rationale: the Builder, Settings, Theme, Preview and Publish tabs all need
/// to read the section list. Passing it through messages would leave each tab
/// maintaining a projection that drifts on rename/reorder. This service owns
/// the data; the tabs own their behaviour. It deliberately has no export,
/// theming or persistence logic -- those live in their own services.
///
/// Threading: intended for UI-thread use only. Mutating from a background
/// thread will raise DocumentChanged off-thread and break WPF bindings.
/// </summary>
public interface IQuizDocumentService
{
    QuizDocument Current { get; }

    /// <summary>True when there are unsaved changes.</summary>
    bool IsDirty { get; }

    /// <summary>Full path of the .qbx this document came from, if any.</summary>
    string? CurrentFilePath { get; }

    event EventHandler<DocumentChangedEventArgs>? DocumentChanged;
    event EventHandler? DirtyStateChanged;

    void NewDocument();

    /// <summary>Replaces the current document wholesale (used by .qbx open).</summary>
    void LoadDocument(QuizDocument document, string? filePath);

    /// <summary>
    /// Replaces the document without clearing the dirty flag or file path.
    /// Used by undo/redo, where the document changes but the file it belongs
    /// to has not.
    /// </summary>
    void RestoreDocument(QuizDocument document);

    void MarkSaved(string filePath);

    void SetTitle(string title);

    /// <summary>
    /// Sets the quiz description. Raises TitleChanged: it is header metadata
    /// like the title, and adding a separate kind would mean every existing
    /// switch on DocumentChangeKind silently ignores it.
    /// </summary>
    void SetDescription(string description);

    Section AddSection(string title);
    void RemoveSection(Guid sectionId);
    void RenameSection(Guid sectionId, string title);

    /// <summary>Moves a section within the authoring order.</summary>
    void MoveSection(Guid sectionId, int newIndex);

    void AddQuestion(Guid sectionId, Question question);

    /// <summary>Appends a blank study card and returns it for editing.</summary>
    StudyCard AddStudyCard();

    /// <summary>Updates a study card's front/back text.</summary>
    void UpdateStudyCard(Guid cardId, string front, string back);

    void RemoveStudyCard(Guid cardId);

    /// <summary>Moves a study card to a new position in the list.</summary>
    void MoveStudyCard(Guid cardId, int newIndex);
    void RemoveQuestion(Guid sectionId, Guid questionId);

    /// <summary>
    /// Moves a question, possibly to a different section. Passing the same
    /// section id reorders within it.
    /// </summary>
    void MoveQuestion(Guid fromSectionId, Guid questionId, Guid toSectionId, int newIndex);

    /// <summary>Signals that a question's contents were edited in place.</summary>
    void NotifyQuestionChanged(Guid sectionId, Guid questionId);

    void SetTheme(string themeId, Theming.ThemeTokens? customTheme);

    void SetSectionDisplayOrder(IEnumerable<Guid> sectionIds);
}
