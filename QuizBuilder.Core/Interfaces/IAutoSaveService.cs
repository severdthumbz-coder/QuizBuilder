namespace QuizBuilder.Core.Interfaces;

public enum AutoSaveOutcome
{
    /// <summary>Saved successfully.</summary>
    Saved,

    /// <summary>Nothing to do: no unsaved changes.</summary>
    NotDirty,

    /// <summary>
    /// The session has never been saved, so there is no path to write to.
    /// Autosave deliberately does NOT invent one: silently creating
    /// Untitled.qbx somewhere would be worse than doing nothing.
    /// </summary>
    NoFilePath,

    /// <summary>Autosave is switched off.</summary>
    Disabled,

    /// <summary>The write failed. See <see cref="AutoSaveEventArgs.Error"/>.</summary>
    Failed
}

public sealed class AutoSaveEventArgs : EventArgs
{
    public AutoSaveEventArgs(AutoSaveOutcome outcome, string? filePath = null, Exception? error = null)
    {
        Outcome = outcome;
        FilePath = filePath;
        Error = error;
        TimestampUtc = DateTimeOffset.UtcNow;
    }

    public AutoSaveOutcome Outcome { get; }
    public string? FilePath { get; }
    public Exception? Error { get; }
    public DateTimeOffset TimestampUtc { get; }
}

/// <summary>
/// Periodically saves the current session to its .qbx file.
///
/// Deliberate constraints:
///
/// - Only saves when a file path already exists. A session that has never been
///   saved has no home, and picking one on the user's behalf (Untitled.qbx in
///   the working directory?) creates files they did not ask for and cannot
///   find. The UI says so rather than pretending to protect work it cannot.
///
/// - Only saves when the document is dirty. An idle timer that rewrites an
///   unchanged file every five minutes burns disk and, on a USB stick, is a
///   genuine wear concern.
///
/// - Never throws into the timer. A failed autosave raises AutoSaved with
///   Failed so the UI can surface it; an unhandled exception on a timer thread
///   would take down the app while the user was doing something else entirely.
/// </summary>
public interface IAutoSaveService
{
    bool IsEnabled { get; }

    /// <summary>Interval in minutes. Ignored when disabled.</summary>
    int IntervalMinutes { get; }

    /// <summary>When the last successful autosave happened, or null.</summary>
    DateTimeOffset? LastSavedUtc { get; }

    /// <summary>Raised after every attempt, successful or not.</summary>
    event EventHandler<AutoSaveEventArgs>? AutoSaved;

    /// <summary>Applies the current settings and starts or stops the timer.</summary>
    void Reconfigure();

    /// <summary>Runs an autosave now, respecting the same skip conditions.</summary>
    Task<AutoSaveOutcome> SaveNowAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the timer. Called on shutdown.</summary>
    void Stop();
}
