namespace QuizBuilder.Core.Models;

/// <summary>
/// A crash-recovery snapshot of the current editing session: the whole document
/// plus the file it came from (if any). Written while the document has unsaved
/// changes and deleted on a clean save or exit, so a snapshot left on disk at
/// startup means the previous session did not end cleanly — a crash, power loss,
/// or force-kill. The user is then offered a chance to restore it.
///
/// The document is stored as its serialized JSON (the same form the .qbx package
/// uses for quiz.json) rather than a full .qbx: the snapshot only needs the
/// authored content, it must be cheap enough to rewrite on every change, and it
/// is written atomically. Images already live in the original package and are
/// referenced by package-relative path, which the restored document keeps.
/// </summary>
public sealed class RecoverySnapshot
{
    /// <summary>Schema version, for future migration of the snapshot format.</summary>
    public int Schema { get; set; } = 1;

    /// <summary>The serialized <see cref="QuizDocument"/>.</summary>
    public string DocumentJson { get; set; } = string.Empty;

    /// <summary>
    /// The .qbx path the session was editing, or null if the quiz had never
    /// been saved. On restore this is reattached so the user can save straight
    /// back to the same file; null means the restored quiz is treated as a new,
    /// never-saved document.
    /// </summary>
    public string? OriginalPath { get; set; }

    /// <summary>When the snapshot was written (UTC), for a "recovered from …" note.</summary>
    public DateTimeOffset SavedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
