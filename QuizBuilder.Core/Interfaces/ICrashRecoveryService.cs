using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// Crash recovery for the desktop editor. Keeps an on-disk snapshot of the
/// current document while it has unsaved changes, so that if the app is killed
/// before the user saves — a crash, power loss, force-quit — the work can be
/// offered back on the next launch.
///
/// The contract is a small state machine (proved in
/// tools/port/crash_recovery_port.py):
/// <list type="bullet">
///   <item>While the document is dirty, <see cref="CaptureSnapshot"/> writes it,
///   atomically, including a brand-new quiz that has never been saved.</item>
///   <item>On a clean save or a clean exit, <see cref="Clear"/> deletes it.</item>
///   <item>At startup, a snapshot that still exists means the last session did
///   NOT end cleanly. <see cref="TryLoadPendingSnapshot"/> returns it; the app
///   offers Restore or Discard.</item>
/// </list>
/// Because a clean shutdown always clears the snapshot, its mere existence at
/// startup is the crash signal — nothing has to track "am I running".
/// </summary>
public interface ICrashRecoveryService
{
    /// <summary>
    /// Write the current document to the recovery snapshot, atomically (a crash
    /// mid-write never leaves a torn snapshot). Call this whenever the document
    /// changes while dirty. <paramref name="originalPath"/> is the current .qbx
    /// path, or null if the quiz has never been saved.
    /// </summary>
    void CaptureSnapshot(QuizDocument document, string? originalPath);

    /// <summary>Delete the snapshot. Idempotent. Called on clean save/exit.</summary>
    void Clear();

    /// <summary>True if a snapshot is present (i.e. the last session crashed).</summary>
    bool HasPendingSnapshot { get; }

    /// <summary>
    /// Return the pending snapshot, or null if none exists or it is corrupt. A
    /// corrupt snapshot is discarded and treated as nothing to recover — crash
    /// recovery must never itself crash startup.
    /// </summary>
    RecoverySnapshot? TryLoadPendingSnapshot();

    /// <summary>
    /// Load the pending snapshot and deserialize its document. Returns the
    /// recovered document and the original file path (null if never saved), or
    /// null if there is nothing to recover or the snapshot is corrupt (in which
    /// case it is discarded). Keeps document serialization inside Core, so the
    /// UI layer never touches the JSON format.
    /// </summary>
    (QuizDocument Document, string? OriginalPath)? TryRecoverDocument();
}
