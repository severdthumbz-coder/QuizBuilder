using System.Text.Json;
using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Attempt history in history.json beside the executable.
///
/// Same portability rule as SettingsService: never %AppData%, never the
/// registry. The file sits next to the .exe and travels with the folder.
/// </summary>
public sealed class AttemptHistoryService : IAttemptHistoryService
{
    private const string FileName = "history.json";

    /// <summary>
    /// Attempts kept per quiz.
    ///
    /// Someone drilling a quiz daily would otherwise grow this file without
    /// limit, and it is read at startup. Fifty sittings is far more history than
    /// anyone reads and still a trivial file; the oldest fall off the end.
    /// </summary>
    private const int MaxAttemptsPerQuiz = 50;

    private readonly string _path;
    private List<AttemptRecord> _attempts = new();

    public AttemptHistoryService(string? overrideDirectory = null)
    {
        _path = Path.Combine(overrideDirectory ?? GetExecutableDirectory(), FileName);
    }

    public event EventHandler? HistoryChanged;

    /// <summary>
    /// AppContext.BaseDirectory, matching SettingsService exactly.
    ///
    /// No Assembly.Location fallback: it returns empty for a single-file bundle
    /// (IL3000), and a "defensive" fallback would quietly resolve history.json
    /// against the working directory instead of the exe.
    /// </summary>
    private static string GetExecutableDirectory() => AppContext.BaseDirectory;

    public IReadOnlyList<AttemptRecord> ForQuiz(Guid quizId) =>
        _attempts
            .Where(a => a.QuizId == quizId)
            .OrderByDescending(a => a.TakenAt)
            .ToList();

    /// <summary>
    /// This quiz's attempts for one taker: records whose stored email key matches
    /// the signed-in taker, plus legacy records that carry no key (shown to
    /// everyone so nothing disappears). Newest first.
    /// </summary>
    public IReadOnlyList<AttemptRecord> ForQuizAndTaker(Guid quizId, string? takerEmailKey) =>
        _attempts
            .Where(a => a.QuizId == quizId && TakerKey.Matches(a.TakerEmailKey, takerEmailKey))
            .OrderByDescending(a => a.TakenAt)
            .ToList();

    public void Add(AttemptRecord attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        _attempts.Add(attempt);

        Trim(attempt.QuizId);
        Save();

        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(Guid attemptId)
    {
        var removed = _attempts.RemoveAll(a => a.Id == attemptId);
        if (removed == 0) return;

        Save();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearForQuiz(Guid quizId)
    {
        var removed = _attempts.RemoveAll(a => a.QuizId == quizId);
        if (removed == 0) return;

        Save();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Trim(Guid quizId)
    {
        var forQuiz = _attempts
            .Where(a => a.QuizId == quizId)
            .OrderByDescending(a => a.TakenAt)
            .ToList();

        if (forQuiz.Count <= MaxAttemptsPerQuiz) return;

        foreach (var old in forQuiz.Skip(MaxAttemptsPerQuiz))
            _attempts.Remove(old);
    }

    public void Load()
    {
        // A missing file is the normal first run, not an error.
        if (!File.Exists(_path))
        {
            _attempts = new List<AttemptRecord>();
            return;
        }

        try
        {
            var json = File.ReadAllText(_path);

            _attempts = JsonSerializer.Deserialize<List<AttemptRecord>>(json, SettingsService.JsonOptions)
                        ?? new List<AttemptRecord>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable history is not worth refusing to start
            // over. The attempts are a record of past sittings, not the user's
            // work -- losing them is a nuisance; failing to open the app is not.
            _attempts = new List<AttemptRecord>();
        }
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_attempts, SettingsService.JsonOptions);

            File.WriteAllText(_path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Read-only media, a locked file, a full disk. Losing an attempt
            // record must not take down the results screen the user is looking
            // at -- the score is already on screen and correct.
        }
    }
}
