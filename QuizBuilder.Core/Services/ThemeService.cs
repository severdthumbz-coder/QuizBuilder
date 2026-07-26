using System.Text.Json;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Theming;

namespace QuizBuilder.Core.Services;

/// <inheritdoc cref="IThemeService"/>
public sealed class ThemeService : IThemeService
{
    /// <summary>Id reserved for the user's custom theme.</summary>
    public const string CustomThemeId = "custom";

    private readonly ISettingsService _settings;
    private ThemeTokens _current;

    /// <summary>
    /// The custom theme as it was when editing began. Null means "there was no
    /// custom theme then", which is a meaningful state: discarding must remove
    /// one created since.
    /// </summary>
    private ThemeTokens? _snapshot;

    /// <summary>
    /// Whether a snapshot has been taken. Distinct from _snapshot being null,
    /// which is itself a valid snapshot value.
    /// </summary>
    private bool _hasSnapshot;

    public ThemeService(ISettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        var themeSettings = _settings.Current.Theme;

        // The custom theme is restored from settings if one was saved.
        Custom = themeSettings.CustomTheme;

        _current = ResolveActive(themeSettings.ActiveThemeId, Custom);
    }

    public ThemeTokens Current => _current;

    public IReadOnlyList<ThemeTokens> BuiltIn => BuiltInThemes.All;

    public ThemeTokens? Custom { get; private set; }

    public bool IsCustomActive =>
        string.Equals(_current.Id, CustomThemeId, StringComparison.OrdinalIgnoreCase);

    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    /// <summary>
    /// Picks the active tokens for a saved id, tolerating a settings file that
    /// names a custom theme which no longer exists, or a built-in id from a
    /// future version. Falling back beats refusing to start.
    /// </summary>
    private static ThemeTokens ResolveActive(string activeId, ThemeTokens? custom)
    {
        if (string.Equals(activeId, CustomThemeId, StringComparison.OrdinalIgnoreCase))
            return custom ?? BuiltInThemes.Academic();

        return BuiltInThemes.ById(activeId);
    }

    public void SelectBuiltIn(string themeId)
    {
        // ById already falls back to Academic for an unknown id.
        _current = BuiltInThemes.ById(themeId);
        _settings.Current.Theme.ActiveThemeId = _current.Id;
        Raise();
    }

    public void CreateCustomFrom(string themeId)
    {
        var source = string.Equals(themeId, CustomThemeId, StringComparison.OrdinalIgnoreCase)
            ? Custom ?? BuiltInThemes.Academic()
            : BuiltInThemes.ById(themeId);

        // Clone, never reference. BuiltInThemes.Academic() returns a fresh
        // object each call today, but relying on that would make this a
        // landmine if the built-ins are ever cached as statics: editing the
        // custom theme would silently mutate the built-in for the whole app.
        Custom = source.Clone();
        Custom.Id = CustomThemeId;
        Custom.DisplayName = "Custom";
        Custom.IsBuiltIn = false;

        _current = Custom;
        _settings.Current.Theme.ActiveThemeId = CustomThemeId;
        _settings.Current.Theme.CustomTheme = Custom;
        Raise();
    }

    public void SelectCustom()
    {
        if (Custom is null) return;

        _current = Custom;
        _settings.Current.Theme.ActiveThemeId = CustomThemeId;
        Raise();
    }

    public void EditCustom(Action<ThemeTokens> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (Custom is null) return;

        edit(Custom);

        // Editing implies the custom theme should be showing; otherwise the
        // user changes a colour and nothing happens, which reads as a bug.
        _current = Custom;
        _settings.Current.Theme.ActiveThemeId = CustomThemeId;
        _settings.Current.Theme.CustomTheme = Custom;
        Raise();
    }

    public void DeleteCustom()
    {
        Custom = null;
        _settings.Current.Theme.CustomTheme = null;

        _current = BuiltInThemes.Academic();
        _settings.Current.Theme.ActiveThemeId = _current.Id;
        Raise();
    }

    public void BeginEdit()
    {
        _snapshot = Custom?.Clone();
        _hasSnapshot = true;
    }

    public void DiscardChanges()
    {
        if (!_hasSnapshot) return;

        Custom = _snapshot?.Clone();
        _settings.Current.Theme.CustomTheme = Custom;

        // A theme created since the snapshot no longer exists, so the app
        // cannot keep showing it.
        if (Custom is null)
        {
            _current = BuiltInThemes.Academic();
            _settings.Current.Theme.ActiveThemeId = _current.Id;
        }
        else
        {
            _current = Custom;
            _settings.Current.Theme.ActiveThemeId = CustomThemeId;
        }

        Raise();
    }

    public bool HasUnsavedChanges
    {
        get
        {
            if (!_hasSnapshot) return false;

            // ThemeTokens is a class, so == is reference equality and Clone()
            // always produces a new object -- a reference compare would report
            // "changed" every time. The tokens are plain serialisable POCOs, so
            // comparing their JSON is a true value compare, and it stays correct
            // when a token is added without anyone remembering to update an
            // Equals override.
            return !string.Equals(Serialise(Custom), Serialise(_snapshot), StringComparison.Ordinal);
        }
    }

    private static string Serialise(ThemeTokens? tokens)
        => tokens is null ? string.Empty : JsonSerializer.Serialize(tokens);

    public void Save()
    {
        _settings.Save();

        // The saved state is the new baseline: without this, HasUnsavedChanges
        // stays true after a save and the Discard button offers to undo changes
        // that are already committed.
        BeginEdit();
    }

    private void Raise() => ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(_current));
}
