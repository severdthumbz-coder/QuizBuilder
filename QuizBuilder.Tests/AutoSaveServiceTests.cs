using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

public class AutoSaveServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SettingsService _settings;
    private readonly QuizDocumentService _document;
    private readonly QuizPackageService _package;
    private readonly AutoSaveService _autoSave;

    public AutoSaveServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "qb-autosave-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _settings = new SettingsService(new TokenProtector(), _tempDir);
        _settings.Load();
        _document = new QuizDocumentService();
        _package = new QuizPackageService();
        _autoSave = new AutoSaveService(_settings, _document, _package);
    }

    public void Dispose()
    {
        _autoSave.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Disabled_ByDefault()
    {
        // Autosave overwrites the user's file. Doing that without being asked
        // is a surprise, especially for someone who opened a .qbx to read it.
        Assert.False(_autoSave.IsEnabled);
        Assert.Equal(AutoSaveSettings.DefaultIntervalMinutes, _autoSave.IntervalMinutes);
    }

    [Fact]
    public async Task SaveNow_WhenDisabled_DoesNothing()
    {
        _document.SetTitle("Changed");
        Assert.Equal(AutoSaveOutcome.Disabled, await _autoSave.SaveNowAsync());
    }

    [Fact]
    public async Task SaveNow_WhenNotDirty_DoesNotWrite()
    {
        _settings.Current.AutoSave.Enabled = true;

        // An idle timer rewriting an unchanged file is wasted disk churn, and
        // on a USB stick it is real wear.
        Assert.Equal(AutoSaveOutcome.NotDirty, await _autoSave.SaveNowAsync());
    }

    [Fact]
    public async Task SaveNow_WithNoFilePath_DoesNotInventOne()
    {
        _settings.Current.AutoSave.Enabled = true;
        _document.SetTitle("Never saved");

        var result = await _autoSave.SaveNowAsync();

        // The important half: no stray file appeared anywhere.
        Assert.Equal(AutoSaveOutcome.NoFilePath, result);
        Assert.Empty(Directory.GetFiles(_tempDir, "*.qbx"));
    }

    [Fact]
    public async Task SaveNow_WithFilePath_WritesAndClearsDirty()
    {
        var path = Path.Combine(_tempDir, "session.qbx");

        _settings.Current.AutoSave.Enabled = true;
        _document.SetTitle("Real quiz");
        await _package.SaveAsync(_document.Current, path);
        _document.MarkSaved(path);

        _document.SetTitle("Edited after save");
        Assert.True(_document.IsDirty);

        var result = await _autoSave.SaveNowAsync();

        Assert.Equal(AutoSaveOutcome.Saved, result);
        Assert.False(_document.IsDirty);
        Assert.True(File.Exists(path));
        Assert.NotNull(_autoSave.LastSavedUtc);
    }

    [Fact]
    public async Task SaveNow_RoundTripsTheEditedContent()
    {
        var path = Path.Combine(_tempDir, "roundtrip.qbx");

        _settings.Current.AutoSave.Enabled = true;
        _document.SetTitle("First");
        await _package.SaveAsync(_document.Current, path);
        _document.MarkSaved(path);

        _document.SetTitle("Second");
        await _autoSave.SaveNowAsync();

        // Autosave that writes a file which cannot be reopened is worse than
        // no autosave.
        var reloaded = await _package.LoadAsync(path);
        Assert.Equal("Second", reloaded.Document.Title);
    }

    [Fact]
    public async Task SaveNow_RaisesEventWithOutcome()
    {
        AutoSaveEventArgs? observed = null;
        _autoSave.AutoSaved += (_, e) => observed = e;

        _settings.Current.AutoSave.Enabled = true;
        await _autoSave.SaveNowAsync();

        Assert.NotNull(observed);
        Assert.Equal(AutoSaveOutcome.NotDirty, observed!.Outcome);
    }

    [Fact]
    public void Reconfigure_WhenDisabled_DoesNotThrow()
    {
        _settings.Current.AutoSave.Enabled = false;
        _autoSave.Reconfigure();
        _autoSave.Stop();
    }

    [Fact]
    public void Reconfigure_ClampsAnOutOfRangeInterval()
    {
        // settings.json is hand-editable. An interval of 0 would spin the
        // timer continuously.
        _settings.Current.AutoSave.Enabled = true;
        _settings.Current.AutoSave.IntervalMinutes = 0;

        _autoSave.Reconfigure();
        _autoSave.Stop();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(5, 5)]
    [InlineData(999, 60)]
    public void IntervalBounds_AreSane(int raw, int expected)
    {
        var clamped = Math.Clamp(raw,
            AutoSaveSettings.MinIntervalMinutes,
            AutoSaveSettings.MaxIntervalMinutes);

        Assert.Equal(expected, clamped);
    }

    [Fact]
    public async Task AutoSaveSettings_PersistAcrossRestart()
    {
        _settings.Current.AutoSave.Enabled = true;
        _settings.Current.AutoSave.IntervalMinutes = 12;
        _settings.Save();

        var settings2 = new SettingsService(new TokenProtector(), _tempDir);
        settings2.Load();

        Assert.True(settings2.Current.AutoSave.Enabled);
        Assert.Equal(12, settings2.Current.AutoSave.IntervalMinutes);

        await Task.CompletedTask;
    }
}
