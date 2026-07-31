using System.Text.Json;

namespace QuizBuilder.Player.Services;

/// <summary>
/// One quiz kept in the player's library: enough to list it without opening the
/// .qbx, plus where its file lives. Keyed on the quiz's own <see cref="QuizId"/>
/// (QuizDocument.Id, stable across a .qbx round trip) so re-importing the same
/// quiz updates this entry instead of duplicating it -- and so it lines up with
/// history and paused attempts, which key on the same id.
/// </summary>
public sealed class LibraryEntry
{
    public Guid QuizId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int QuestionCount { get; set; }

    /// <summary>The stored file's name inside the library directory (not a full
    /// path: the directory can move between app versions, the name cannot).</summary>
    public string FileName { get; set; } = string.Empty;

    public DateTimeOffset AddedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// The player's kept collection of quizzes. Persists an index (library.json) and
/// the quiz files beside it in the app sandbox, so quizzes survive between runs
/// and the taker picks from a list instead of re-importing a file every time.
///
/// <para>
/// This supersedes the old "copy to a throwaway import_*.qbx and prune later"
/// model: files here are kept deliberately, named by quiz id, and removed only
/// when the taker deletes the quiz.
/// </para>
/// </summary>
public sealed class QuizLibraryService
{
    private const string IndexFileName = "library.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly List<LibraryEntry> _entries = new();
    private bool _loaded;

    private string LibraryDir => FileSystem.AppDataDirectory;
    private string IndexPath => Path.Combine(LibraryDir, IndexFileName);

    /// <summary>The stored file path for a quiz id. Public so the session can
    /// load a chosen quiz through the same Core path a fresh import uses.</summary>
    public string FilePathFor(Guid quizId) =>
        Path.Combine(LibraryDir, FileNameFor(quizId));

    private static string FileNameFor(Guid quizId) => $"quiz_{quizId:N}.qbx";

    /// <summary>Reads the index once. A missing or corrupt file is a normal
    /// empty library, not an error -- the same forgiving stance the other
    /// sandbox stores take.</summary>
    public void Load()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            if (!File.Exists(IndexPath)) return;
            var json = File.ReadAllText(IndexPath);
            var entries = JsonSerializer.Deserialize<List<LibraryEntry>>(json, JsonOptions);
            if (entries is not null)
            {
                _entries.Clear();
                // Drop index rows whose file has gone missing, so the list never
                // offers a quiz that cannot be opened.
                _entries.AddRange(entries.Where(e => File.Exists(Path.Combine(LibraryDir, e.FileName))));
            }
        }
        catch
        {
            // Corrupt index: start empty rather than fail to launch.
            _entries.Clear();
        }
    }

    /// <summary>Library entries, newest activity first.</summary>
    public IReadOnlyList<LibraryEntry> Entries
    {
        get
        {
            Load();
            return _entries.OrderByDescending(e => e.UpdatedAt).ToList();
        }
    }

    public bool Has(Guid quizId)
    {
        Load();
        return _entries.Any(e => e.QuizId == quizId);
    }

    /// <summary>
    /// Adds or updates a quiz in the library from an already-loaded .qbx sitting
    /// at <paramref name="sourcePath"/>. The file is copied to the library under
    /// its quiz-id name (replacing any earlier copy of the same quiz), and the
    /// index row is inserted or refreshed. Returns the stored entry.
    /// </summary>
    public LibraryEntry AddOrUpdate(Guid quizId, string title, int questionCount, string sourcePath)
    {
        Load();
        Directory.CreateDirectory(LibraryDir);

        var fileName = FileNameFor(quizId);
        var destination = Path.Combine(LibraryDir, fileName);

        // Copy the bytes into the library. If the source already IS the stored
        // file (re-saving in place), skip the copy.
        if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destination),
                StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, destination, overwrite: true);
        }

        var now = DateTimeOffset.Now;
        var existing = _entries.FirstOrDefault(e => e.QuizId == quizId);
        if (existing is null)
        {
            existing = new LibraryEntry
            {
                QuizId = quizId,
                FileName = fileName,
                AddedAt = now,
            };
            _entries.Add(existing);
        }

        existing.Title = title;
        existing.QuestionCount = questionCount;
        existing.UpdatedAt = now;

        Save();
        return existing;
    }

    /// <summary>
    /// Removes a quiz from the library and deletes its file. History and paused
    /// data are NOT touched here -- the caller decides that separately, because
    /// the taker is asked each time whether to keep or wipe it.
    /// </summary>
    public void Remove(Guid quizId)
    {
        Load();

        var entry = _entries.FirstOrDefault(e => e.QuizId == quizId);
        if (entry is null) return;

        try
        {
            var path = Path.Combine(LibraryDir, entry.FileName);
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // A locked/gone file must not block removing the index row.
        }

        _entries.Remove(entry);
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(LibraryDir);
            File.WriteAllText(IndexPath, JsonSerializer.Serialize(_entries, JsonOptions));
        }
        catch
        {
            // Best-effort: a failed index write leaves the in-memory list intact
            // for this run; it will be retried on the next change.
        }
    }
}
