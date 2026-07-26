using System.Text.Json;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Stores the reusable question bank in question-bank.json beside the exe,
/// following the same shape as <see cref="AttemptHistoryService"/> and
/// <see cref="PausedAttemptService"/>: an in-memory list persisted on every
/// change, tolerant of a missing or corrupt file.
/// </summary>
public sealed class QuestionBankService : IQuestionBankService
{
    private const string FileName = "question-bank.json";

    private readonly string _path;
    private List<BankEntry> _entries = new();

    public QuestionBankService(string? overrideDirectory = null)
    {
        _path = Path.Combine(overrideDirectory ?? AppContext.BaseDirectory, FileName);
    }

    public event EventHandler? BankChanged;

    public IReadOnlyList<BankEntry> All() =>
        _entries.OrderByDescending(e => e.AddedUtc).ToList();

    public IReadOnlyList<string> Categories() =>
        _entries
            .Select(e => e.Category)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public BankEntry Add(Question question, string? category)
    {
        ArgumentNullException.ThrowIfNull(question);

        // Clone on the way in so the bank owns an independent copy. Clone() also
        // mints a fresh question id, and images are dropped by not carrying a
        // package: ImageRelativePath rides along on the clone but points at a
        // package the bank does not have, so it is cleared to keep bank questions
        // genuinely text-only and self-contained.
        var stored = question.Clone();
        stored.ImageRelativePath = null;

        var entry = new BankEntry
        {
            Question = stored,
            Category = Normalise(category),
        };

        _entries.Add(entry);
        Persist();
        BankChanged?.Invoke(this, EventArgs.Empty);

        return entry;
    }

    public void SetCategory(Guid entryId, string? category)
    {
        var entry = _entries.FirstOrDefault(e => e.Id == entryId);
        if (entry is null) return;

        entry.Category = Normalise(category);
        Persist();
        BankChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(Guid entryId)
    {
        if (_entries.RemoveAll(e => e.Id == entryId) > 0)
        {
            Persist();
            BankChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Load()
    {
        if (!File.Exists(_path))
        {
            _entries = new List<BankEntry>();
            return;
        }

        try
        {
            var json = File.ReadAllText(_path);

            _entries = JsonSerializer.Deserialize<List<BankEntry>>(json, SettingsService.JsonOptions)
                       ?? new List<BankEntry>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable bank should not stop the app; start empty.
            _entries = new List<BankEntry>();
        }
    }

    private static string? Normalise(string? category)
    {
        var trimmed = category?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private void Persist()
    {
        try
        {
            var json = JsonSerializer.Serialize(_entries, SettingsService.JsonOptions);

            File.WriteAllText(_path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed save loses the change, not the app.
        }
    }
}
