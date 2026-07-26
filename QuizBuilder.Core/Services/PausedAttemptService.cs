using System.Text.Json;
using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Stores paused sittings in paused-attempts.json beside the exe, mirroring
/// <see cref="AttemptHistoryService"/>. Kept separate from history because the
/// two have opposite lifecycles: history only grows and is read-only once
/// written; a paused attempt is transient, saved and then removed the moment it
/// is resumed to completion or discarded.
/// </summary>
public sealed class PausedAttemptService : IPausedAttemptService
{
    private const string FileName = "paused-attempts.json";

    /// <summary>
    /// A generous ceiling so someone who repeatedly pauses without finishing
    /// cannot grow the file without bound. Paused attempts are meant to be
    /// resumed soon, so this is far more than anyone should accumulate; the
    /// oldest fall off the end.
    /// </summary>
    private const int MaxPausedAttempts = 50;

    private readonly string _path;
    private List<PausedAttempt> _attempts = new();

    public PausedAttemptService(string? overrideDirectory = null)
    {
        _path = Path.Combine(overrideDirectory ?? AppContext.BaseDirectory, FileName);
    }

    public event EventHandler? PausedAttemptsChanged;

    public IReadOnlyList<PausedAttempt> ForQuiz(Guid quizId) =>
        _attempts
            .Where(a => a.QuizId == quizId)
            .OrderByDescending(a => a.SavedAt)
            .ToList();

    public void Save(PausedAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        // Replace any earlier save of the same sitting: re-pausing updates in
        // place rather than piling up stale snapshots of one attempt.
        _attempts.RemoveAll(a => a.Id == attempt.Id);
        _attempts.Add(attempt);

        // Trim oldest beyond the cap.
        if (_attempts.Count > MaxPausedAttempts)
        {
            _attempts = _attempts
                .OrderByDescending(a => a.SavedAt)
                .Take(MaxPausedAttempts)
                .ToList();
        }

        Persist();
        PausedAttemptsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(Guid attemptId)
    {
        var removed = _attempts.RemoveAll(a => a.Id == attemptId) > 0;

        if (removed)
        {
            Persist();
            PausedAttemptsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Load()
    {
        if (!File.Exists(_path))
        {
            _attempts = new List<PausedAttempt>();
            return;
        }

        try
        {
            var json = File.ReadAllText(_path);

            _attempts = JsonSerializer.Deserialize<List<PausedAttempt>>(json, SettingsService.JsonOptions)
                        ?? new List<PausedAttempt>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable file loses paused sittings, which is a
            // nuisance, not a reason to refuse to start.
            _attempts = new List<PausedAttempt>();
        }
    }

    private void Persist()
    {
        try
        {
            var json = JsonSerializer.Serialize(_attempts, SettingsService.JsonOptions);

            File.WriteAllText(_path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only media, a locked file, a full disk. A failed save must not
            // take down the sitting the user is in the middle of.
        }
    }
}
