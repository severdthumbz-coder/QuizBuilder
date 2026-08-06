using System.Text.Json;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <summary>
/// A JSON-file implementation of <see cref="IReviewProgressStore"/>. Follows the
/// same shape as <see cref="PausedAttemptService"/>: load once, keep in memory,
/// write the whole list on each change. Progress is small (a few numbers per
/// reviewed card), so rewriting the file wholesale is simple and safe.
///
/// The file sits beside the app (or a supplied directory), NOT in the .qbx —
/// review progress is personal and must not travel with a shared quiz.
/// </summary>
public sealed class ReviewProgressStore : IReviewProgressStore
{
    private const string FileName = "review-progress.json";

    private readonly string _path;
    private readonly object _gate = new();
    private List<ReviewState> _states = new();
    private bool _loaded;

    public ReviewProgressStore(string? overrideDirectory = null)
    {
        _path = Path.Combine(overrideDirectory ?? AppContext.BaseDirectory, FileName);
    }

    public event EventHandler? ProgressChanged;

    public ReviewState? Get(Guid quizId, Guid cardId)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _states.FirstOrDefault(s => s.QuizId == quizId && s.CardId == cardId);
        }
    }

    public IReadOnlyList<ReviewState> ForQuiz(Guid quizId)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _states.Where(s => s.QuizId == quizId).ToList();
        }
    }

    public void Save(ReviewState state)
    {
        lock (_gate)
        {
            EnsureLoaded();
            _states.RemoveAll(s => s.QuizId == state.QuizId && s.CardId == state.CardId);
            _states.Add(state);
            Persist();
        }

        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearQuiz(Guid quizId)
    {
        bool removed;
        lock (_gate)
        {
            EnsureLoaded();
            removed = _states.RemoveAll(s => s.QuizId == quizId) > 0;
            if (removed) Persist();
        }

        if (removed) ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                _states = JsonSerializer.Deserialize<List<ReviewState>>(json, SettingsService.JsonOptions)
                          ?? new List<ReviewState>();
            }
        }
        catch
        {
            // A corrupt or unreadable file must never crash study; start fresh.
            // The next Save rewrites it cleanly.
            _states = new List<ReviewState>();
        }
    }

    private void Persist()
    {
        try
        {
            var json = JsonSerializer.Serialize(_states, SettingsService.JsonOptions);
            File.WriteAllText(_path, json);
        }
        catch
        {
            // Best-effort persistence; losing a write is recoverable (the schedule
            // just doesn't advance) and must not interrupt a study session.
        }
    }
}
