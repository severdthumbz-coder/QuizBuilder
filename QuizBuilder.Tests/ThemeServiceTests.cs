using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;
using QuizBuilder.Core.Theming;
using Xunit;

namespace QuizBuilder.Tests;

public class ThemeServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _settings;
    private readonly ThemeService _themes;

    public ThemeServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "qb-theme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _settings = new SettingsService(new TokenProtector(), _tempDir);
        _settings.Load();
        _themes = new ThemeService(_settings);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void DefaultTheme_IsAcademic()
    {
        Assert.Equal(BuiltInThemes.AcademicId, _themes.Current.Id);
        Assert.False(_themes.IsCustomActive);
    }

    [Fact]
    public void SelectBuiltIn_ChangesCurrentAndRaises()
    {
        ThemeTokens? observed = null;
        _themes.ThemeChanged += (_, e) => observed = e.Tokens;

        _themes.SelectBuiltIn(BuiltInThemes.DarkExamId);

        Assert.Equal(BuiltInThemes.DarkExamId, _themes.Current.Id);
        Assert.NotNull(observed);
        Assert.True(_themes.Current.IsDark);
    }

    [Fact]
    public void SelectBuiltIn_UnknownId_FallsBackWithoutThrowing()
    {
        _themes.SelectBuiltIn("no-such-theme");

        // A settings file naming a theme from a newer version must not stop
        // the app starting.
        Assert.Equal(BuiltInThemes.AcademicId, _themes.Current.Id);
    }

    [Fact]
    public void CreateCustomFrom_DoesNotMutateTheBuiltIn()
    {
        // The bug this guards: if the custom theme referenced the built-in
        // instead of cloning it, editing a colour would corrupt the built-in
        // for the entire application, silently.
        var originalPrimary = BuiltInThemes.Academic().Colors.Primary;

        _themes.CreateCustomFrom(BuiltInThemes.AcademicId);
        _themes.EditCustom(t => t.Colors.Primary = "#FF0000");

        Assert.Equal("#FF0000", _themes.Custom!.Colors.Primary);
        Assert.Equal(originalPrimary, BuiltInThemes.Academic().Colors.Primary);
    }

    [Fact]
    public void CreateCustomFrom_DeepClonesNestedTokens()
    {
        _themes.CreateCustomFrom(BuiltInThemes.PlayfulId);

        var builtIn = BuiltInThemes.Playful();
        _themes.EditCustom(t =>
        {
            t.Typography.BaseSize = 99;
            t.Spacing.Unit = 99;
            t.Shape.RadiusMd = 99;
        });

        // A shallow clone would let these leak into the built-in.
        Assert.NotEqual(99, builtIn.Typography.BaseSize);
        Assert.NotEqual(99, builtIn.Spacing.Unit);
        Assert.NotEqual(99, builtIn.Shape.RadiusMd);
    }

    [Fact]
    public void CreateCustomFrom_ClonesElevationArrays()
    {
        _themes.CreateCustomFrom(BuiltInThemes.AcademicId);

        var builtIn = BuiltInThemes.Academic();
        _themes.EditCustom(t => t.Shape.ElevationOpacity[0] = 0.99);

        // MemberwiseClone would share the array reference: this is the case
        // that a "clone" which looks correct still gets wrong.
        Assert.NotEqual(0.99, builtIn.Shape.ElevationOpacity[0]);
    }

    [Fact]
    public void CustomTheme_SurvivesSwitchingAway()
    {
        _themes.CreateCustomFrom(BuiltInThemes.AcademicId);
        _themes.EditCustom(t => t.Colors.Primary = "#123456");

        _themes.SelectBuiltIn(BuiltInThemes.CorporateId);
        Assert.False(_themes.IsCustomActive);

        _themes.SelectCustom();

        Assert.True(_themes.IsCustomActive);
        Assert.Equal("#123456", _themes.Current.Colors.Primary);
    }

    [Fact]
    public void DeleteCustom_FallsBackToBuiltIn()
    {
        _themes.CreateCustomFrom(BuiltInThemes.AcademicId);
        _themes.DeleteCustom();

        Assert.Null(_themes.Custom);
        Assert.False(_themes.IsCustomActive);
        Assert.Equal(BuiltInThemes.AcademicId, _themes.Current.Id);
    }

    [Fact]
    public void SelectCustom_WithNoCustom_IsNoOp()
    {
        _themes.SelectBuiltIn(BuiltInThemes.CorporateId);
        _themes.SelectCustom();

        Assert.Equal(BuiltInThemes.CorporateId, _themes.Current.Id);
    }

    [Fact]
    public void CustomTheme_PersistsAcrossServiceRestart()
    {
        _themes.CreateCustomFrom(BuiltInThemes.DarkExamId);
        _themes.EditCustom(t => t.Colors.Accent = "#ABCDEF");
        _themes.Save();

        // Simulate a restart: fresh settings + service reading the same file.
        var settings2 = new SettingsService(new TokenProtector(), _tempDir);
        settings2.Load();
        var themes2 = new ThemeService(settings2);

        Assert.True(themes2.IsCustomActive);
        Assert.Equal("#ABCDEF", themes2.Current.Colors.Accent);
    }

    [Fact]
    public void SettingsFile_IsActuallyWritten()
    {
        _themes.SelectBuiltIn(BuiltInThemes.PlayfulId);
        _themes.Save();

        // The probe reported "exists: False" for the whole shell slice, because
        // nothing had called Save yet. This pins that it now does.
        Assert.True(File.Exists(_settings.SettingsFilePath));
    }

    // --- Save / Discard -----------------------------------------------------

    [Fact]
    public void DiscardPutsBackTheColourEditingStartedWith()
    {
        // The bug this fixes: every edit went straight to disk, so an
        // experiment was permanent the instant it was made. The only escape was
        // DeleteCustom, which throws away the whole theme rather than the last
        // few changes.
        _themes.CreateCustomFrom(BuiltInThemes.AcademicId);
        _themes.Save();

        var committed = _themes.Custom!.Colors.Primary;

        _themes.BeginEdit();
        _themes.EditCustom(t => t.Colors.Primary = "#FF0000");

        Assert.Equal("#FF0000", _themes.Custom!.Colors.Primary);

        _themes.DiscardChanges();

        Assert.Equal(committed, _themes.Custom!.Colors.Primary);
    }

    [Fact]
    public void EditingAloneDoesNotTouchDisk()
    {
        // The fixture writes to a real temp directory, so this can check the
        // actual file rather than a proxy for it.
        _themes.CreateCustomFrom(BuiltInThemes.AcademicId);
        _themes.EditCustom(t => t.Colors.Primary = "#0000FF");
        _themes.Save();

        _themes.BeginEdit();
        _themes.EditCustom(t => t.Colors.Primary = "#FF0000");

        // A separate service reading the same directory sees only what was
        // committed. This is the whole point of the fix: an experiment lives in
        // memory until the user says otherwise.
        var onDisk = new SettingsService(new TokenProtector(), _tempDir);
        onDisk.Load();

        Assert.Equal("#0000FF", onDisk.Current.Theme.CustomTheme!.Colors.Primary);
        Assert.Equal("#FF0000", _themes.Custom!.Colors.Primary);
    }

    [Fact]
    public void SaveWritesTheEditToDisk()
    {
        _themes.CreateCustomFrom(BuiltInThemes.AcademicId);
        _themes.Save();

        _themes.BeginEdit();
        _themes.EditCustom(t => t.Colors.Primary = "#FF0000");
        _themes.Save();

        var onDisk = new SettingsService(new TokenProtector(), _tempDir);
        onDisk.Load();

        Assert.Equal("#FF0000", onDisk.Current.Theme.CustomTheme!.Colors.Primary);
    }

    [Fact]
    public void DiscardLeavesDiskAlone()
    {
        _themes.CreateCustomFrom(BuiltInThemes.AcademicId);
        _themes.EditCustom(t => t.Colors.Primary = "#0000FF");
        _themes.Save();

        _themes.BeginEdit();
        _themes.EditCustom(t => t.Colors.Primary = "#FF0000");
        _themes.DiscardChanges();

        var onDisk = new SettingsService(new TokenProtector(), _tempDir);
        onDisk.Load();

        Assert.Equal("#0000FF", onDisk.Current.Theme.CustomTheme!.Colors.Primary);
        Assert.Equal("#0000FF", _themes.Custom!.Colors.Primary);
    }

    [Fact]
    public void SaveClearsTheUnsavedFlag()
    {
        _themes.CreateCustomFrom(BuiltInThemes.AcademicId);
        _themes.BeginEdit();
        _themes.EditCustom(t => t.Colors.Primary = "#FF0000");

        Assert.True(_themes.HasUnsavedChanges);

        _themes.Save();

        // Save re-baselines, or Discard would offer to undo committed work.
        Assert.False(_themes.HasUnsavedChanges);
    }

    [Fact]
    public void DiscardingAThemeThatWasNeverSavedRemovesItEntirely()
    {
        // Snapshot taken while there was no custom theme at all. Discarding
        // should leave no trace, not an empty shell.
        _themes.BeginEdit();
        _themes.CreateCustomFrom(BuiltInThemes.AcademicId);
        _themes.EditCustom(t => t.Colors.Primary = "#FF0000");

        Assert.NotNull(_themes.Custom);

        _themes.DiscardChanges();

        Assert.Null(_themes.Custom);
        Assert.False(_themes.IsCustomActive);
    }

    [Fact]
    public void HasUnsavedChangesComparesByValueNotReference()
    {
        // ThemeTokens is a class and Clone() returns a new object, so a
        // reference compare would report "changed" even when nothing was
        // touched, leaving Save and Discard permanently enabled.
        _themes.CreateCustomFrom(BuiltInThemes.AcademicId);
        _themes.BeginEdit();

        Assert.False(_themes.HasUnsavedChanges);
    }

    [Fact]
    public void NoSnapshotMeansNothingToDiscard()
    {
        _themes.CreateCustomFrom(BuiltInThemes.AcademicId);

        // BeginEdit was never called: HasUnsavedChanges must not claim changes
        // it has no baseline for.
        Assert.False(_themes.HasUnsavedChanges);
    }

    [Fact]
    public void DiscardRaisesThemeChangedSoTheUiRepaints()
    {
        _themes.CreateCustomFrom(BuiltInThemes.AcademicId);
        _themes.Save();
        _themes.BeginEdit();
        _themes.EditCustom(t => t.Colors.Primary = "#FF0000");

        var raised = 0;
        _themes.ThemeChanged += (_, _) => raised++;

        _themes.DiscardChanges();

        // Without this the colours revert in memory but the window keeps
        // showing the discarded theme.
        Assert.Equal(1, raised);
    }
}
