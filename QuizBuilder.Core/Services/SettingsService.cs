using System.Text.Json;
using System.Text.Json.Serialization;
using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Loads/saves <see cref="AppSettings"/> to settings.json beside the executable.
/// Never touches %AppData% or the registry -- the app is portable by design.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private const string FileName = "settings.json";
    private const int MaxRecentFiles = 10;

    private readonly TokenProtector _protector;
    private readonly string _settingsPath;
    private AppSettings _current = new();

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsService(TokenProtector protector, string? overrideDirectory = null)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _settingsPath = Path.Combine(overrideDirectory ?? GetExecutableDirectory(), FileName);
    }

    public AppSettings Current => _current;

    public string SettingsFilePath => _settingsPath;

    public event EventHandler? SettingsChanged;

    public bool RequiresPassphrase => _protector.RequiresPassphrase;

    /// <summary>
    /// Directory containing the running executable.
    ///
    /// AppContext.BaseDirectory is the only correct answer for a single-file
    /// app: it reports the directory the .exe actually sits in. There is
    /// deliberately no Assembly.Location fallback: that property returns an
    /// empty string when assemblies are embedded in a single-file bundle
    /// (IL3000), so a "defensive" fallback would silently resolve
    /// settings.json against the current working directory instead of the exe.
    /// A portable app that writes its settings to wherever the shortcut
    /// happened to start from is broken in a way nothing reports.
    ///
    /// If BaseDirectory is ever empty the app is in a state we cannot honour
    /// the portability contract from, so fail loudly rather than guess.
    /// </summary>
    private static string GetExecutableDirectory()
    {
        var dir = AppContext.BaseDirectory;

        if (string.IsNullOrEmpty(dir))
            throw new InvalidOperationException(
                "Could not determine the application directory. Quiz Builder stores " +
                "settings.json beside its executable and cannot start without knowing " +
                "where that is.");

        return dir;
    }

    public void Load()
    {
        if (!File.Exists(_settingsPath))
        {
            _current = new AppSettings();
            ApplyProtectionMode();
            return;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            _current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // A corrupt settings file must not prevent the app starting.
            // Preserve the bad file for diagnosis rather than silently
            // overwriting it -- the user may have hand-edited it.
            TryBackupCorruptFile();
            _current = new AppSettings();
        }
        catch (IOException)
        {
            _current = new AppSettings();
        }

        ApplyProtectionMode();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Syncs the protector to the persisted mode and hands it the stored
    /// ciphertext, so RequiresPassphrase can be answered before any prompt.
    /// </summary>
    private void ApplyProtectionMode()
    {
        _protector.SetMode(_current.GitHub.TokenProtection);
        _protector.SetPendingCipherText(_current.GitHub.EncryptedToken);
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            var backup = _settingsPath + ".corrupt-" +
                         DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            File.Copy(_settingsPath, backup, overwrite: true);
        }
        catch (IOException) { /* best effort only */ }
        catch (UnauthorizedAccessException) { /* best effort only */ }
    }

    /// <summary>
    /// Writes settings atomically: serialize to a temp file in the same
    /// directory, then replace. A half-written settings.json from a crash or
    /// a yanked USB stick is worse than a slightly stale one.
    /// </summary>
    public void Save()
    {
        var json = JsonSerializer.Serialize(_current, JsonOptions);
        var tempPath = _settingsPath + ".tmp";

        try
        {
            File.WriteAllText(tempPath, json);

            if (File.Exists(_settingsPath))
            {
                // File.Replace is atomic on NTFS. It fails on some removable
                // filesystems (FAT32 on a USB stick), so fall back to a
                // delete+move, which is the best available there.
                try
                {
                    File.Replace(tempPath, _settingsPath, destinationBackupFileName: null);
                }
                catch (IOException)
                {
                    File.Delete(_settingsPath);
                    File.Move(tempPath, _settingsPath);
                }
                catch (PlatformNotSupportedException)
                {
                    File.Delete(_settingsPath);
                    File.Move(tempPath, _settingsPath);
                }
            }
            else
            {
                File.Move(tempPath, _settingsPath);
            }

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch (IOException) { }
            }
        }
    }

    public void AddRecentFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var recent = _current.Publish.RecentFiles;

        // De-duplicate case-insensitively: Windows paths are case-insensitive,
        // so "C:\Quiz.qbx" and "c:\quiz.qbx" are the same file.
        recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, path);

        if (recent.Count > MaxRecentFiles)
            recent.RemoveRange(MaxRecentFiles, recent.Count - MaxRecentFiles);
    }

    public void SetGitHubToken(string? plainTextToken)
    {
        if (string.IsNullOrEmpty(plainTextToken))
        {
            _current.GitHub.EncryptedToken = null;
            _protector.SetPendingCipherText(null);
            return;
        }

        // Throws in Passphrase mode when locked; the caller must unlock first.
        _current.GitHub.EncryptedToken = _protector.Protect(plainTextToken);
        _protector.SetPendingCipherText(_current.GitHub.EncryptedToken);
    }

    public string? GetGitHubToken() => _protector.Unprotect(_current.GitHub.EncryptedToken);

    public bool UnlockTokens(string passphrase) => _protector.Unlock(passphrase);

    public void SetTokenProtectionMode(TokenProtectionMode mode)
    {
        if (_current.GitHub.TokenProtection == mode) return;

        // Ciphertext is not transcoded between modes: doing so would need the
        // old mode unlocked and the new one configured simultaneously. Clear
        // it instead and let the user re-enter. The UI must warn before this.
        _current.GitHub.TokenProtection = mode;
        _current.GitHub.EncryptedToken = null;

        _protector.SetMode(mode);
        _protector.SetPendingCipherText(null);

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
