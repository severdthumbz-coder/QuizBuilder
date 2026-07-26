using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.Core.Services;

/// <inheritdoc cref="IAutoSaveService"/>
public sealed class AutoSaveService : IAutoSaveService, IDisposable
{
    private readonly ISettingsService _settings;
    private readonly IQuizDocumentService _document;
    private readonly IQuizPackageService _package;

    private Timer? _timer;

    /// <summary>
    /// Guards against overlapping saves. A .qbx write on a slow USB stick can
    /// outlast a short interval; a second write starting while the first is
    /// mid-flight would race on the temp file and could leave the archive
    /// truncated. SemaphoreSlim(1,1) with a zero-wait means a tick that
    /// arrives during a save is simply skipped, which is the right call: the
    /// next tick will catch it.
    /// </summary>
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private bool _disposed;

    public AutoSaveService(
        ISettingsService settings,
        IQuizDocumentService document,
        IQuizPackageService package)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _package = package ?? throw new ArgumentNullException(nameof(package));
    }

    private AutoSaveSettings Config => _settings.Current.AutoSave;

    public bool IsEnabled => Config.Enabled;

    public int IntervalMinutes => Config.IntervalMinutes;

    public DateTimeOffset? LastSavedUtc { get; private set; }

    public event EventHandler<AutoSaveEventArgs>? AutoSaved;

    public void Reconfigure()
    {
        if (_disposed) return;

        _timer?.Dispose();
        _timer = null;

        if (!Config.Enabled) return;

        // Clamp defensively: the value comes from a JSON file a user can edit
        // by hand, and an interval of 0 would spin the timer continuously.
        var minutes = Math.Clamp(Config.IntervalMinutes,
                                 AutoSaveSettings.MinIntervalMinutes,
                                 AutoSaveSettings.MaxIntervalMinutes);

        var period = TimeSpan.FromMinutes(minutes);

        // Fire first after one full interval, not immediately: an autosave the
        // instant the setting is switched on would surprise the user.
        _timer = new Timer(OnTick, null, period, period);
    }

    /// <summary>
    /// Timer callback. Async void by necessity (Timer takes a void callback),
    /// so it must never let an exception escape: an unhandled exception on a
    /// timer thread terminates the process, and the user would lose everything
    /// while doing something unrelated. Every failure path is caught and
    /// reported through the AutoSaved event instead.
    /// </summary>
    private async void OnTick(object? state)
    {
        try
        {
            await SaveNowAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Raise(new AutoSaveEventArgs(AutoSaveOutcome.Failed, error: ex));
        }
    }

    public async Task<AutoSaveOutcome> SaveNowAsync(CancellationToken cancellationToken = default)
    {
        if (!Config.Enabled)
            return Report(AutoSaveOutcome.Disabled);

        if (!_document.IsDirty)
            return Report(AutoSaveOutcome.NotDirty);

        var path = _document.CurrentFilePath;
        if (string.IsNullOrEmpty(path))
        {
            // No path: the session has never been saved. Autosave will not
            // choose a location on the user's behalf.
            return Report(AutoSaveOutcome.NoFilePath);
        }

        // Skip rather than queue when a save is already running.
        if (!await _saveLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return AutoSaveOutcome.NotDirty;

        try
        {
            await _package.SaveAsync(
                _document.Current, path,
                imageResolver: null,
                cancellationToken).ConfigureAwait(false);

            _document.MarkSaved(path);
            LastSavedUtc = DateTimeOffset.UtcNow;

            return Report(AutoSaveOutcome.Saved, path);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A locked file, a removed USB stick, a full disk. None of these
            // should be fatal: report and let the next tick try again.
            return Report(AutoSaveOutcome.Failed, path, ex);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private AutoSaveOutcome Report(AutoSaveOutcome outcome, string? path = null, Exception? error = null)
    {
        Raise(new AutoSaveEventArgs(outcome, path, error));
        return outcome;
    }

    private void Raise(AutoSaveEventArgs args) => AutoSaved?.Invoke(this, args);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _timer?.Dispose();
        _timer = null;
        _saveLock.Dispose();
    }
}
