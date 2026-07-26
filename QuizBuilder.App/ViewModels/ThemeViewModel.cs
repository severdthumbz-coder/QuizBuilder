using System.Collections.ObjectModel;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;
using QuizBuilder.Core.Theming;

namespace QuizBuilder.App.ViewModels;

/// <summary>One theme in the picker.</summary>
public sealed class ThemeChoice : ViewModelBase
{
    private bool _isActive;

    public ThemeChoice(ThemeTokens tokens)
    {
        Tokens = tokens;
        Id = tokens.Id;
        DisplayName = tokens.DisplayName;
    }

    public ThemeTokens Tokens { get; }
    public string Id { get; }
    public string DisplayName { get; }

    // Swatch colours for the preview chips. Exposed as strings; the view's
    // converter turns them into brushes, so this stays WPF-free.
    public string PrimaryHex => Tokens.Colors.Primary;
    public string AccentHex => Tokens.Colors.Accent;
    public string SurfaceHex => Tokens.Colors.Surface;
    public string TextHex => Tokens.Colors.TextPrimary;
    public string BackgroundHex => Tokens.Colors.Background;

    public string FontSummary => Tokens.Typography.FontFamily.Split(',')[0].Trim();

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}

/// <summary>
/// Theme and layout.
///
/// Owns theme selection and the custom editor. It does NOT own the quiz, the
/// settings file, or the WPF resource dictionary: it calls IThemeService, and
/// the App layer listens for ThemeChanged and rebuilds resources. That keeps
/// this testable and keeps the token system WPF-free.
/// </summary>
public sealed class ThemeViewModel : ViewModelBase
{
    private readonly IThemeService _themes;
    private readonly ISettingsService _settings;

    public ThemeViewModel(IThemeService themes, ISettingsService settings)
    {
        _themes = themes ?? throw new ArgumentNullException(nameof(themes));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        Choices = new ObservableCollection<ThemeChoice>(
            _themes.BuiltIn.Select(t => new ThemeChoice(t)));

        SelectThemeCommand = new RelayCommand(p =>
        {
            if (p is ThemeChoice choice) SelectTheme(choice.Id);
        });

        CustomiseCommand = new RelayCommand(
            () =>
            {
                // Snapshot BEFORE creating: the thing to go back to is the state
                // as it was when the user started, which may be "no custom theme
                // at all". Snapshotting after would record the new theme as the
                // baseline and make discard a no-op.
                _themes.BeginEdit();
                _themes.CreateCustomFrom(_themes.Current.Id);
            },
            () => true);

        UseCustomCommand = new RelayCommand(
            () =>
            {
                _themes.SelectCustom();

                // Persist: switching to the custom theme is a choice, like
                // picking a built-in. Only edits to its colours are deferred.
                _themes.Save();
            },
            () => _themes.Custom is not null);

        DeleteCustomCommand = new RelayCommand(
            () =>
            {
                _themes.DeleteCustom();

                // Persist immediately. A deleted theme that comes back after a
                // restart would be alarming -- and Save() re-baselines the
                // snapshot, so Discard cannot resurrect it either.
                _themes.Save();
            },
            () => _themes.Custom is not null);

        SaveCustomCommand = new RelayCommand(
            () => _themes.Save(),
            () => _themes.HasUnsavedChanges);

        DiscardCustomCommand = new RelayCommand(
            () => _themes.DiscardChanges(),
            () => _themes.HasUnsavedChanges);

        _themes.ThemeChanged += (_, _) => OnThemeChanged();

        RefreshActiveStates();
    }

    public ObservableCollection<ThemeChoice> Choices { get; }

    public RelayCommand SelectThemeCommand { get; }
    public RelayCommand CustomiseCommand { get; }
    public RelayCommand UseCustomCommand { get; }
    public RelayCommand DeleteCustomCommand { get; }
    public RelayCommand SaveCustomCommand { get; }
    public RelayCommand DiscardCustomCommand { get; }

    /// <summary>Drives the "unsaved changes" hint and the Save/Discard buttons.</summary>
    public bool HasUnsavedChanges => _themes.HasUnsavedChanges;

    public string CustomStatus => _themes.HasUnsavedChanges
        ? "Unsaved changes."
        : _themes.Custom is null ? string.Empty : "Saved.";

    public bool HasCustom => _themes.Custom is not null;
    public bool IsCustomActive => _themes.IsCustomActive;

    public string ActiveThemeName => _themes.Current.DisplayName;

    // --- Custom theme editor ---------------------------------------------
    //
    // Each setter routes through IThemeService.EditCustom so the change is
    // applied, persisted and broadcast in one step. Guarded on HasCustom:
    // binding a slider to a null custom theme would throw on first render.

    public string CustomPrimary
    {
        get => _themes.Custom?.Colors.Primary ?? _themes.Current.Colors.Primary;
        set => EditColour(c => c.Primary = value);
    }

    public string CustomAccent
    {
        get => _themes.Custom?.Colors.Accent ?? _themes.Current.Colors.Accent;
        set => EditColour(c => c.Accent = value);
    }

    public string CustomFontFamily
    {
        get => _themes.Custom?.Typography.FontFamily ?? _themes.Current.Typography.FontFamily;
        set
        {
            if (_themes.Custom is null || string.IsNullOrWhiteSpace(value)) return;
            _themes.EditCustom(t => t.Typography.FontFamily = value);
            OnPropertyChanged();
        }
    }

    public double CustomFontSize
    {
        get => _themes.Custom?.Typography.BaseSize ?? _themes.Current.Typography.BaseSize;
        set
        {
            if (_themes.Custom is null) return;

            // Clamp rather than trust the slider: a bound value can arrive from
            // a restored settings file too, and a 0 or negative base size makes
            // every derived size in the ramp collapse.
            var clamped = Math.Clamp(value, MinFontSize, MaxFontSize);
            _themes.EditCustom(t => t.Typography.BaseSize = clamped);
            OnPropertyChanged();
        }
    }

    public double CustomCornerRadius
    {
        get => _themes.Custom?.Shape.RadiusMd ?? _themes.Current.Shape.RadiusMd;
        set
        {
            if (_themes.Custom is null) return;

            var clamped = Math.Clamp(value, MinRadius, MaxRadius);
            _themes.EditCustom(t =>
            {
                // Keep the radius scale proportional rather than setting only
                // the medium value: a theme with RadiusMd=20 and RadiusSm=2
                // looks broken, not customised.
                t.Shape.RadiusSm = Math.Round(clamped * 0.5);
                t.Shape.RadiusMd = clamped;
                t.Shape.RadiusLg = Math.Round(clamped * 1.75);
            });
            OnPropertyChanged();
        }
    }

    public double CustomSpacingUnit
    {
        get => _themes.Custom?.Spacing.Unit ?? _themes.Current.Spacing.Unit;
        set
        {
            if (_themes.Custom is null) return;

            var clamped = Math.Clamp(value, MinSpacing, MaxSpacing);
            _themes.EditCustom(t => t.Spacing.Unit = clamped);
            OnPropertyChanged();
        }
    }

    // Bounds exposed for the sliders, so the XAML cannot drift from the clamps.
    public double MinFontSize => 10;
    public double MaxFontSize => 22;
    public double MinRadius => 0;
    public double MaxRadius => 20;
    public double MinSpacing => 4;
    public double MaxSpacing => 16;

    private void EditColour(Action<ColorTokens> edit)
    {
        if (_themes.Custom is null) return;
        _themes.EditCustom(t => edit(t.Colors));
        OnPropertyChanged();
    }

    private void SelectTheme(string id)
    {
        if (string.Equals(id, ThemeService.CustomThemeId, StringComparison.OrdinalIgnoreCase))
            _themes.SelectCustom();
        else
            _themes.SelectBuiltIn(id);

        // Save here, explicitly.
        //
        // OnThemeChanged used to do this for every theme event, which is what
        // made the custom editor unable to discard. Removing it from there left
        // this path silently not persisting: picking a built-in theme would look
        // right until the app restarted. Choosing a theme is a decision, not an
        // experiment -- there is nothing to discard, so it commits immediately.
        _themes.Save();
    }

    private void OnThemeChanged()
    {
        RefreshActiveStates();

        OnPropertyChanged(nameof(HasCustom));
        OnPropertyChanged(nameof(IsCustomActive));
        OnPropertyChanged(nameof(ActiveThemeName));
        OnPropertyChanged(nameof(CustomPrimary));
        OnPropertyChanged(nameof(CustomAccent));
        OnPropertyChanged(nameof(CustomFontFamily));
        OnPropertyChanged(nameof(CustomFontSize));
        OnPropertyChanged(nameof(CustomCornerRadius));
        OnPropertyChanged(nameof(CustomSpacingUnit));

        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(CustomStatus));

        RelayCommand.RaiseCanExecuteChanged();

        // Deliberately NOT saving here.
        //
        // This used to call _themes.Save() on every change, which meant the
        // custom editor had no way back: every experiment was committed to disk
        // the instant it was made, and the only escape was DeleteCustom -- which
        // throws away the entire theme rather than the last few edits.
        //
        // Selecting a BUILT-IN theme still saves immediately (see SelectTheme):
        // that is a choice, not an experiment, and there is nothing to discard.
        // Only the custom editor defers.
    }

    private void RefreshActiveStates()
    {
        foreach (var choice in Choices)
            choice.IsActive = !_themes.IsCustomActive
                              && string.Equals(choice.Id, _themes.Current.Id,
                                               StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Font stacks offered in the editor. First entry wins, rest are fallbacks.</summary>
    public IReadOnlyList<string> FontOptions { get; } = new[]
    {
        "Segoe UI, Inter, system-ui, sans-serif",
        "Georgia, Cambria, Times New Roman, serif",
        "Verdana, Trebuchet MS, Segoe UI, sans-serif",
        "Consolas, Cascadia Mono, monospace",
        "Calibri, Candara, Segoe UI, sans-serif",
    };
}
