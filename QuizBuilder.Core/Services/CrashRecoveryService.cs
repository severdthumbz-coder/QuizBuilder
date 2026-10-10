using System.Text.Json;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <summary>
/// File-backed crash recovery. Mirrors the state machine proved in
/// tools/port/crash_recovery_port.py. The snapshot lives beside the app (or a
/// supplied directory) as a single JSON file; its presence at startup is the
/// crash signal.
///
/// Writes are ATOMIC: the snapshot is written to a temp file in the same
/// directory, flushed to disk, then moved over the real path. A crash before the
/// move leaves the previous good snapshot (or none); a crash after leaves the new
/// one. There is never a half-written snapshot at the real path — which matters
/// precisely because this file is only ever read after a crash.
/// </summary>
public sealed class CrashRecoveryService : ICrashRecoveryService
{
    private const string FileName = "recovery-snapshot.json";

    private readonly string _directory;
    private readonly string _path;
    private readonly object _gate = new();

    public CrashRecoveryService(string? overrideDirectory = null)
    {
        _directory = overrideDirectory ?? AppContext.BaseDirectory;
        _path = Path.Combine(_directory, FileName);
    }

    public bool HasPendingSnapshot => File.Exists(_path);

    public void CaptureSnapshot(QuizDocument document, string? originalPath)
    {
        ArgumentNullException.ThrowIfNull(document);

        var snapshot = new RecoverySnapshot
        {
            DocumentJson = JsonSerializer.Serialize(document, SettingsService.JsonOptions),
            OriginalPath = originalPath,
            SavedAtUtc = DateTimeOffset.UtcNow,
        };

        lock (_gate)
        {
            try
            {
                WriteAtomic(snapshot);
            }
            catch
            {
                // Best-effort: failing to write a recovery snapshot must never
                // interrupt editing. The next change tries again. (A real save
                // or autosave is the user's primary protection; this is the
                // extra net for the unsaved window.)
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(_path)) File.Delete(_path);
            }
            catch
            {
                // Idempotent and best-effort. A snapshot we can't delete is a
                // false "crash" on next launch at worst — the user discards it.
            }
        }
    }

    public RecoverySnapshot? TryLoadPendingSnapshot()
    {
        lock (_gate)
        {
            if (!File.Exists(_path)) return null;

            try
            {
                var json = File.ReadAllText(_path);
                var snapshot = JsonSerializer.Deserialize<RecoverySnapshot>(json, SettingsService.JsonOptions);

                // A structurally-valid file that carries no document is useless;
                // treat it as corrupt.
                if (snapshot is null || string.IsNullOrEmpty(snapshot.DocumentJson))
                {
                    DiscardQuietly();
                    return null;
                }

                return snapshot;
            }
            catch
            {
                // Corrupt or unreadable: discard and recover nothing. Recovery
                // must never crash startup.
                DiscardQuietly();
                return null;
            }
        }
    }

    public (QuizDocument Document, string? OriginalPath)? TryRecoverDocument()
    {
        var snapshot = TryLoadPendingSnapshot();
        if (snapshot is null) return null;

        try
        {
            var document = JsonSerializer.Deserialize<QuizDocument>(
                snapshot.DocumentJson, SettingsService.JsonOptions);

            if (document is null)
            {
                Clear();
                return null;
            }

            return (document, snapshot.OriginalPath);
        }
        catch
        {
            // The outer JSON parsed but the embedded document did not; useless.
            Clear();
            return null;
        }
    }

    private void WriteAtomic(RecoverySnapshot snapshot)
    {
        Directory.CreateDirectory(_directory);

        var tmp = Path.Combine(_directory, $".recovery-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, snapshot, SettingsService.JsonOptions);
                stream.Flush(flushToDisk: true);   // bytes on disk before the move
            }

            // Atomic replace: overwrite:true moves tmp over the real path in one
            // step on the same volume. Never a torn file at _path.
            File.Move(tmp, _path, overwrite: true);
        }
        catch
        {
            TryDelete(tmp);
            throw;
        }
    }

    private void DiscardQuietly()
    {
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
        }
        catch
        {
            // Nothing more to do; leaving a stale corrupt file only means the
            // next launch offers a recovery the user will discard.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Temp litter cleanup is best-effort.
        }
    }
}
