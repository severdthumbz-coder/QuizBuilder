using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Theming;

namespace QuizBuilder.Core.Services;

/// <inheritdoc cref="IQuizDocumentService"/>
public sealed class QuizDocumentService : IQuizDocumentService
{
    private QuizDocument _current = new();
    private bool _isDirty;
    private string? _currentFilePath;

    public QuizDocument Current => _current;

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value) return;
            _isDirty = value;
            DirtyStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string? CurrentFilePath => _currentFilePath;

    public event EventHandler<DocumentChangedEventArgs>? DocumentChanged;
    public event EventHandler? DirtyStateChanged;

    public void NewDocument()
    {
        _current = new QuizDocument();
        _currentFilePath = null;
        Raise(DocumentChangeKind.DocumentReplaced);
        IsDirty = false;
    }

    /// <summary>
    /// Replaces the document without treating it as a fresh load: the file
    /// path is kept and the document stays dirty.
    /// <para>
    /// Undo uses this rather than <see cref="LoadDocument"/>, which clears the
    /// dirty flag. Undoing back to the last-saved arrangement does not mean
    /// the file on disk matches -- other edits may have been saved since --
    /// so reporting "no unsaved changes" there would be a lie that costs the
    /// user work at the next close prompt.
    /// </para>
    /// </summary>
    public void RestoreDocument(QuizDocument document)
    {
        _current = document ?? throw new ArgumentNullException(nameof(document));
        Raise(DocumentChangeKind.DocumentReplaced);
        IsDirty = true;
    }

    public void LoadDocument(QuizDocument document, string? filePath)
    {
        _current = document ?? throw new ArgumentNullException(nameof(document));
        _currentFilePath = filePath;
        Raise(DocumentChangeKind.DocumentReplaced);
        IsDirty = false;
    }

    public void MarkSaved(string filePath)
    {
        _currentFilePath = filePath;
        IsDirty = false;
    }

    public void SetTitle(string title)
    {
        var value = title ?? string.Empty;
        if (_current.Title == value) return;
        _current.Title = value;
        Raise(DocumentChangeKind.TitleChanged);
    }

    public void SetDescription(string description)
    {
        // No blank coercion: an empty description is a normal state, not a
        // mistake to correct. That also keeps a PropertyChanged binding safe,
        // since nothing rewrites the text under the caret.
        var value = description ?? string.Empty;
        if (_current.Description == value) return;

        _current.Description = value;
        Raise(DocumentChangeKind.TitleChanged);
    }

    public Section AddSection(string title)
    {
        var section = new Section
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled Section" : title
        };
        _current.Sections.Add(section);
        _current.SectionDisplayOrder.Add(section.Id);
        Raise(DocumentChangeKind.SectionAdded, section.Id);
        return section;
    }

    public void RemoveSection(Guid sectionId)
    {
        var section = FindSection(sectionId);
        if (section is null) return;

        _current.Sections.Remove(section);
        _current.SectionDisplayOrder.Remove(sectionId);
        Raise(DocumentChangeKind.SectionRemoved, sectionId);
    }

    public void RenameSection(Guid sectionId, string title)
    {
        var section = FindSection(sectionId);
        if (section is null) return;

        var value = string.IsNullOrWhiteSpace(title) ? "Untitled Section" : title;
        if (section.Title == value) return;

        section.Title = value;
        Raise(DocumentChangeKind.SectionRenamed, sectionId);
    }

    public void MoveSection(Guid sectionId, int newIndex)
    {
        var section = FindSection(sectionId);
        if (section is null) return;

        var oldIndex = _current.Sections.IndexOf(section);
        if (oldIndex < 0) return;

        // Clamp rather than throw: drag-and-drop routinely produces an index
        // one past the end when dropping below the last item.
        newIndex = Math.Clamp(newIndex, 0, _current.Sections.Count - 1);
        if (oldIndex == newIndex) return;

        _current.Sections.RemoveAt(oldIndex);
        _current.Sections.Insert(newIndex, section);

        // Rebuild the display order from the reordered list. SectionDisplayOrder
        // takes priority in SectionsInDisplayOrder(), so leaving it stale would
        // let the Quiz Builder tab show one order while an exported PDF used
        // another -- with nothing thrown and no way to notice until it printed.
        _current.SectionDisplayOrder = _current.Sections.Select(s => s.Id).ToList();

        Raise(DocumentChangeKind.SectionsReordered, sectionId);
    }

    public void AddQuestion(Guid sectionId, Question question)
    {
        ArgumentNullException.ThrowIfNull(question);
        var section = FindSection(sectionId);
        if (section is null) return;

        section.Questions.Add(question);
        Raise(DocumentChangeKind.QuestionAdded, sectionId, question.Id);
    }

    public StudyCard AddStudyCard()
    {
        var card = new StudyCard();
        _current.StudyCards.Add(card);

        Raise(DocumentChangeKind.StudyCardsChanged);
        return card;
    }

    public void UpdateStudyCard(Guid cardId, string front, string back)
    {
        var card = _current.StudyCards.FirstOrDefault(c => c.Id == cardId);
        if (card is null) return;

        // No-op guard: the editor fires on every keystroke, and re-raising with
        // identical text would mark the document dirty for nothing and churn the
        // deferred deck rebuild.
        if (card.Front == front && card.Back == back) return;

        card.Front = front;
        card.Back = back;

        Raise(DocumentChangeKind.StudyCardsChanged);
    }

    public void RemoveStudyCard(Guid cardId)
    {
        var removed = _current.StudyCards.RemoveAll(c => c.Id == cardId);
        if (removed == 0) return;

        Raise(DocumentChangeKind.StudyCardsChanged);
    }

    public void MoveStudyCard(Guid cardId, int newIndex)
    {
        var oldIndex = _current.StudyCards.FindIndex(c => c.Id == cardId);
        if (oldIndex < 0) return;

        var clamped = Math.Clamp(newIndex, 0, _current.StudyCards.Count - 1);
        if (clamped == oldIndex) return;

        var card = _current.StudyCards[oldIndex];
        _current.StudyCards.RemoveAt(oldIndex);
        _current.StudyCards.Insert(clamped, card);

        Raise(DocumentChangeKind.StudyCardsChanged);
    }

    public void RemoveQuestion(Guid sectionId, Guid questionId)
    {
        var section = FindSection(sectionId);
        var question = section?.Questions.FirstOrDefault(q => q.Id == questionId);
        if (section is null || question is null) return;

        section.Questions.Remove(question);
        Raise(DocumentChangeKind.QuestionRemoved, sectionId, questionId);
    }

    public void MoveQuestion(Guid fromSectionId, Guid questionId, Guid toSectionId, int newIndex)
    {
        var from = FindSection(fromSectionId);
        var to = FindSection(toSectionId);
        if (from is null || to is null) return;

        var question = from.Questions.FirstOrDefault(q => q.Id == questionId);
        if (question is null) return;

        var oldIndex = from.Questions.IndexOf(question);
        from.Questions.RemoveAt(oldIndex);

        // Clamp against the destination's post-removal count. When moving
        // within one section this is Count-after-removal; across sections it
        // is the destination's own Count. Both are handled by using `to`
        // after the removal has already happened.
        newIndex = Math.Clamp(newIndex, 0, to.Questions.Count);
        to.Questions.Insert(newIndex, question);

        Raise(DocumentChangeKind.QuestionsReordered, toSectionId, questionId);
    }

    public void NotifyQuestionChanged(Guid sectionId, Guid questionId)
        => Raise(DocumentChangeKind.QuestionChanged, sectionId, questionId);

    public void SetTheme(string themeId, ThemeTokens? customTheme)
    {
        _current.ThemeId = string.IsNullOrWhiteSpace(themeId) ? BuiltInThemes.AcademicId : themeId;
        _current.CustomTheme = customTheme;
        Raise(DocumentChangeKind.ThemeChanged);
    }

    public void SetSectionDisplayOrder(IEnumerable<Guid> sectionIds)
    {
        ArgumentNullException.ThrowIfNull(sectionIds);
        _current.SectionDisplayOrder = sectionIds.ToList();
        Raise(DocumentChangeKind.DisplayOrderChanged);
    }

    private Section? FindSection(Guid id) => _current.Sections.FirstOrDefault(s => s.Id == id);

    private void Raise(DocumentChangeKind kind, Guid? sectionId = null, Guid? questionId = null)
    {
        _current.ModifiedUtc = DateTimeOffset.UtcNow;
        DocumentChanged?.Invoke(this, new DocumentChangedEventArgs(kind, sectionId, questionId));

        // DocumentReplaced is followed by an explicit IsDirty = false in the
        // caller; everything else dirties the document.
        if (kind != DocumentChangeKind.DocumentReplaced)
            IsDirty = true;
    }
}
