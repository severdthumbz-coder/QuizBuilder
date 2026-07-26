namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// Undo/redo for structural changes to the document: sections, questions and
/// study cards added, removed, reordered or renamed.
///
/// <para>
/// Snapshot-based. Each step holds a full serialised copy of the
/// <see cref="Models.QuizDocument"/> taken immediately <i>before</i> a
/// mutation. That is far simpler than a command pattern with an inverse per
/// operation, and the document is small enough that the memory cost is
/// invisible at the default depth.
/// </para>
///
/// <para>
/// Deliberately <b>not</b> covering in-editor typing. A TextBox already
/// provides its own undo while focused, and mixing the two scopes makes
/// Ctrl+Z unpredictable: the user cannot tell whether it will retract a
/// character or remove a whole section. Keystroke-level edits arrive as
/// <see cref="DocumentChangeKind.QuestionChanged"/>, which this ignores.
/// </para>
///
/// <para>
/// Threading: UI-thread only, matching <see cref="IQuizDocumentService"/>.
/// </para>
/// </summary>
public interface IUndoService
{
    bool CanUndo { get; }
    bool CanRedo { get; }

    /// <summary>
    /// Number of steps currently retained. Exposed so the settings tab can
    /// show what is actually held rather than only the configured ceiling.
    /// </summary>
    int UndoDepth { get; }

    int RedoDepth { get; }

    /// <summary>
    /// Short description of what undoing would reverse, e.g. "Delete section".
    /// Null when there is nothing to undo. Lets the UI offer "Undo Delete
    /// section" rather than a bare "Undo".
    /// </summary>
    string? NextUndoLabel { get; }

    string? NextRedoLabel { get; }

    /// <summary>Raised whenever the availability or depth of undo/redo changes.</summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Captures the current document as an undo step. Call immediately
    /// <i>before</i> mutating, since the snapshot is the state being returned
    /// to. Clears the redo stack: once a new edit happens, the branch that
    /// redo would have replayed no longer exists.
    /// </summary>
    void CaptureBeforeChange(string label);

    /// <summary>
    /// Restores the previous snapshot. Returns false when there was nothing to
    /// undo, so callers need not check <see cref="CanUndo"/> first.
    /// </summary>
    bool Undo();

    bool Redo();

    /// <summary>
    /// Discards all history. Called when a different document is loaded --
    /// undoing across an Open into a document that is no longer on screen
    /// would be incoherent.
    /// </summary>
    void Clear();

    /// <summary>
    /// Applies a new depth limit, trimming existing history immediately.
    /// Trimming lazily would let the setting misreport what is retained.
    /// </summary>
    void SetDepth(int depth);
}
