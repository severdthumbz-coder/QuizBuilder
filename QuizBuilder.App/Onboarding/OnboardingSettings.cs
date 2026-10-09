using System;
using System.IO;
using System.Text.Json;

namespace OnboardingKit;

/// <summary>
/// Where the two onboarding flags live. Implement this over your app's own
/// settings object (recommended — one settings file), or use
/// <see cref="JsonFileOnboardingStore"/> for a zero-effort standalone file.
/// </summary>
public interface IOnboardingSettingsStore
{
    /// <summary>Master switch for the ⓘ hints, the ❔ legend button and the first-run tour. Default: true.</summary>
    bool ShowOnboardingHelpers { get; set; }

    /// <summary>Set to true once the tour has been finished or skipped, so it never auto-starts again.</summary>
    bool HasSeenTour { get; set; }

    /// <summary>Persist the two flags. Called after every change. May fire-and-forget an async save.</summary>
    void Save();
}

/// <summary>
/// Stand-alone store: keeps the flags in a small JSON file (default: <c>onboarding.json</c>
/// next to the EXE, matching the portable-app convention). Use this if you don't want to
/// touch your existing settings class.
/// </summary>
public sealed class JsonFileOnboardingStore : IOnboardingSettingsStore
{
    private sealed class Data
    {
        public bool ShowOnboardingHelpers { get; set; } = true;
        public bool HasSeenTour { get; set; }
    }

    private readonly string _path;
    private readonly Data _data;

    public JsonFileOnboardingStore(string? path = null)
    {
        _path = path ?? Path.Combine(AppContext.BaseDirectory, "onboarding.json");
        try
        {
            _data = File.Exists(_path)
                ? JsonSerializer.Deserialize<Data>(File.ReadAllText(_path)) ?? new Data()
                : new Data();
        }
        catch { _data = new Data(); }   // corrupt file → defaults (helpers on, tour unseen)
    }

    public bool ShowOnboardingHelpers { get => _data.ShowOnboardingHelpers; set => _data.ShowOnboardingHelpers = value; }
    public bool HasSeenTour { get => _data.HasSeenTour; set => _data.HasSeenTour = value; }

    public void Save()
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true })); }
        catch { /* read-only folder etc. — onboarding state is non-critical */ }
    }
}

/// <summary>
/// Adapter for an existing settings object without writing a class:
/// <code>new DelegateOnboardingStore(
///     () => App.ConfigService.Settings.ShowOnboardingHelpers, v => App.ConfigService.Settings.ShowOnboardingHelpers = v,
///     () => App.ConfigService.Settings.HasSeenTour,           v => App.ConfigService.Settings.HasSeenTour = v,
///     () => _ = App.ConfigService.SaveAsync());</code>
/// </summary>
public sealed class DelegateOnboardingStore : IOnboardingSettingsStore
{
    private readonly Func<bool> _getHelpers; private readonly Action<bool> _setHelpers;
    private readonly Func<bool> _getSeen; private readonly Action<bool> _setSeen;
    private readonly Action _save;

    public DelegateOnboardingStore(Func<bool> getHelpers, Action<bool> setHelpers,
                                   Func<bool> getSeen, Action<bool> setSeen, Action save)
    {
        _getHelpers = getHelpers; _setHelpers = setHelpers;
        _getSeen = getSeen; _setSeen = setSeen; _save = save;
    }

    public bool ShowOnboardingHelpers { get => _getHelpers(); set => _setHelpers(value); }
    public bool HasSeenTour { get => _getSeen(); set => _setSeen(value); }
    public void Save() => _save();
}

/// <summary>In-memory store used until <see cref="OnboardingController.Initialize"/> is called.</summary>
internal sealed class MemoryOnboardingStore : IOnboardingSettingsStore
{
    public bool ShowOnboardingHelpers { get; set; } = true;
    public bool HasSeenTour { get; set; } = true;   // never auto-start before Initialize
    public void Save() { }
}
