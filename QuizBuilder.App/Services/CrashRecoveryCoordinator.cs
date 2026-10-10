using System.Windows;
using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.App.Services;

/// <summary>
/// Desktop glue for crash recovery. Core's <see cref="ICrashRecoveryService"/>
/// does the file state machine; this wires it to the live editing session:
///
/// <list type="bullet">
///   <item>While the document is dirty, every change re-captures the snapshot
///   (atomically, via Core). This covers brand-new, never-saved quizzes too.</item>
///   <item>When the document becomes clean — a real save — the snapshot is
///   cleared.</item>
///   <item>On a clean app exit the snapshot is cleared, so its presence at the
///   next launch means the previous session crashed.</item>
///   <item>At startup, a leftover snapshot triggers a Restore / Discard prompt;
///   Restore loads the recovered document (dirty, with its original path), and
///   Discard deletes it.</item>
/// </list>
///
/// Keeping this in the App layer (not Core) is deliberate: it touches WPF
/// (MessageBox) and the document service's events, while Core stays WPF-free.
/// </summary>
public sealed class CrashRecoveryCoordinator
{
    private readonly ICrashRecoveryService _recovery;
    private readonly IQuizDocumentService _document;

    public CrashRecoveryCoordinator(
        ICrashRecoveryService recovery,
        IQuizDocumentService document)
    {
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    /// <summary>
    /// Begin watching the document. Call once at startup, AFTER any recovery
    /// prompt — so restoring a snapshot (which marks the document dirty) is
    /// itself immediately captured, keeping a crash-after-restore covered.
    /// </summary>
    public void Start()
    {
        _document.DocumentChanged += OnDocumentChanged;
        _document.DirtyStateChanged += OnDirtyStateChanged;

        // If we started already dirty (e.g. a restore just happened), capture now.
        if (_document.IsDirty)
            Capture();
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        // Re-snapshot on every change while there are unsaved edits. A clean
        // document has nothing to protect; its snapshot was cleared when it
        // went clean.
        if (_document.IsDirty)
            Capture();
    }

    private void OnDirtyStateChanged(object? sender, EventArgs e)
    {
        if (_document.IsDirty)
            Capture();      // first edit of a clean document
        else
            _recovery.Clear();   // a save (or new/empty document) — nothing to recover
    }

    private void Capture()
    {
        _recovery.CaptureSnapshot(_document.Current, _document.CurrentFilePath);
    }

    /// <summary>Clear the snapshot on a clean exit. Call from App.OnExit.</summary>
    public void ClearForCleanExit() => _recovery.Clear();

    /// <summary>
    /// If a snapshot is pending (the last session crashed), ask the user whether
    /// to restore it. Call once at startup, BEFORE <see cref="Start"/>. Returns
    /// true if a document was restored.
    /// </summary>
    public bool CheckForRecovery()
    {
        if (!_recovery.HasPendingSnapshot) return false;

        // Peek first, for the prompt text (what file, when). A null here means
        // the snapshot was absent or corrupt — already discarded by Core.
        var snapshot = _recovery.TryLoadPendingSnapshot();
        if (snapshot is null) return false;

        var where = string.IsNullOrEmpty(snapshot.OriginalPath)
            ? "a quiz that had not been saved yet"
            : $"\"{System.IO.Path.GetFileName(snapshot.OriginalPath)}\"";

        var when = snapshot.SavedAtUtc.ToLocalTime().ToString("f");

        var choice = MessageBox.Show(
            $"Quiz Builder didn't close normally last time, and there are unsaved changes to {where} " +
            $"from {when}.\n\nRestore them?",
            "Recover unsaved work",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        if (choice != MessageBoxResult.Yes)
        {
            // Discard: forget the snapshot and start fresh.
            _recovery.Clear();
            return false;
        }

        // Core deserializes the document (serialization stays in Core). Load it
        // dirty, reattaching the original path. The snapshot is intentionally
        // NOT cleared here: the restored work is still unsaved, so a crash before
        // the next save stays recoverable. A real save clears it.
        var recovered = _recovery.TryRecoverDocument();
        if (recovered is not { } r)
        {
            _recovery.Clear();
            return false;
        }

        _document.RestoreRecoveredDocument(r.Document, r.OriginalPath);
        return true;
    }
}
