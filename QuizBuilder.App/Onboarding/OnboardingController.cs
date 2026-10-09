using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace OnboardingKit;

/// <summary>
/// The single brain of the onboarding kit. One shared instance (<see cref="Current"/>) so every
/// control — and any XAML anywhere — can bind to it with
/// <c>{Binding X, Source={x:Static ob:OnboardingController.Current}}</c> regardless of DataContext.
/// <para>Call <see cref="Initialize"/> once at startup, then <see cref="MaybeAutoStartTour"/> when the
/// main window has finished loading.</para>
/// </summary>
public sealed class OnboardingController : INotifyPropertyChanged
{
    public static OnboardingController Current { get; } = new();

    private IOnboardingSettingsStore _store = new MemoryOnboardingStore();
    private List<TourStep> _steps = new();
    private Action<int>? _selectTab;

    private OnboardingController()
    {
        StartTourCommand = new OnbCommand(StartTour, () => _steps.Count > 0);
        NextCommand = new OnbCommand(Next);
        BackCommand = new OnbCommand(Back, () => !IsFirstStep);
        SkipCommand = new OnbCommand(FinishTour);
        ToggleLegendCommand = new OnbCommand(() => IsLegendOpen = !IsLegendOpen);
        ResetFirstRunCommand = new OnbCommand(ResetFirstRun);
    }

    // ───────────────────────── setup ─────────────────────────

    /// <param name="store">Where ShowOnboardingHelpers / HasSeenTour persist.</param>
    /// <param name="steps">The tour cards, in order.</param>
    /// <param name="legendItems">Rows for the ❔ Legend (optional).</param>
    /// <param name="selectTab">How to switch tabs for a step's TabIndex, e.g. <c>i => vm.SelectedTabIndex = i</c>
    /// or <c>i => MainTabs.SelectedIndex = i</c>. Optional.</param>
    public void Initialize(IOnboardingSettingsStore store,
                           IEnumerable<TourStep> steps,
                           IEnumerable<LegendItem>? legendItems = null,
                           Action<int>? selectTab = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _steps = steps?.ToList() ?? new List<TourStep>();
        _selectTab = selectTab;

        LegendItems.Clear();
        if (legendItems != null)
            foreach (var item in legendItems) LegendItems.Add(item);

        OnChanged(nameof(ShowHelpers));
        OnChanged(nameof(HasSeenTour));
        RaiseStep();
    }

    /// <summary>Change the tab-switch callback later (e.g. once the window exists).</summary>
    public void SetTabSelector(Action<int>? selectTab) => _selectTab = selectTab;

    // ───────────────────────── master switch ─────────────────────────

    /// <summary>
    /// Bind your Settings checkbox to this. Off = hides every ⓘ hint and the ❔ legend button,
    /// stops the first-run tour, and closes anything open. Persists immediately.
    /// The Help-tab "Replay tour" button keeps working either way.
    /// </summary>
    public bool ShowHelpers
    {
        get => _store.ShowOnboardingHelpers;
        set
        {
            if (_store.ShowOnboardingHelpers == value) return;
            _store.ShowOnboardingHelpers = value;
            _store.Save();
            if (!value)
            {
                IsLegendOpen = false;
                if (IsTourActive) FinishTour();
            }
            OnChanged();
        }
    }

    public bool HasSeenTour => _store.HasSeenTour;

    // ───────────────────────── tour ─────────────────────────

    public IReadOnlyList<TourStep> Steps => _steps;

    private bool _isTourActive;
    public bool IsTourActive
    {
        get => _isTourActive;
        private set { if (_isTourActive == value) return; _isTourActive = value; OnChanged(); }
    }

    private int _index;
    public int StepIndex
    {
        get => _index;
        private set
        {
            if (_steps.Count == 0) return;
            _index = Math.Clamp(value, 0, _steps.Count - 1);
            RaiseStep();
            ApplyStep();
        }
    }

    public TourStep? CurrentStep => _index >= 0 && _index < _steps.Count ? _steps[_index] : null;
    public string Progress => _steps.Count == 0 ? "" : $"Step {_index + 1} of {_steps.Count}";
    public bool IsFirstStep => _index <= 0;
    public bool IsLastStep => _index >= _steps.Count - 1;

    public ICommand StartTourCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SkipCommand { get; }
    public ICommand ResetFirstRunCommand { get; }

    /// <summary>Raised whenever a step is shown (after the tab switch).</summary>
    public event EventHandler<TourStep>? StepShown;
    /// <summary>Raised when the tour finishes or is skipped.</summary>
    public event EventHandler? TourEnded;

    /// <summary>Start (or replay) from step 1. Works even when helpers are switched off.</summary>
    public void StartTour()
    {
        if (_steps.Count == 0) return;
        IsLegendOpen = false;
        StepIndex = 0;          // raises + switches tab for step 0 too
        IsTourActive = true;
    }

    public void Next()
    {
        if (!IsTourActive) return;
        if (IsLastStep) FinishTour();
        else StepIndex = _index + 1;
    }

    public void Back()
    {
        if (IsTourActive && !IsFirstStep) StepIndex = _index - 1;
    }

    /// <summary>Close the tour (Finish or Skip) and remember it, so it never auto-starts again.</summary>
    public void FinishTour()
    {
        IsTourActive = false;
        if (!_store.HasSeenTour)
        {
            _store.HasSeenTour = true;
            _store.Save();
            OnChanged(nameof(HasSeenTour));
        }
        TourEnded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Make the tour auto-start again on the next launch (handy for testing / support).</summary>
    public void ResetFirstRun()
    {
        _store.HasSeenTour = false;
        _store.Save();
        OnChanged(nameof(HasSeenTour));
    }

    /// <summary>
    /// First-run auto-start. Call from MainWindow.Loaded via
    /// <c>Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, ...)</c>
    /// (or just call <see cref="ScheduleAutoStart"/>), so the window is fully painted first.
    /// </summary>
    public void MaybeAutoStartTour()
    {
        if (_store.ShowOnboardingHelpers && !_store.HasSeenTour && !IsTourActive)
            StartTour();
    }

    /// <summary>Convenience: queues <see cref="MaybeAutoStartTour"/> at ApplicationIdle on the UI thread.</summary>
    public void ScheduleAutoStart(Dispatcher? dispatcher = null)
    {
        var d = dispatcher ?? Application.Current?.Dispatcher;
        d?.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(MaybeAutoStartTour));
    }

    private void ApplyStep()
    {
        var step = CurrentStep;
        if (step == null) return;
        if (step.TabIndex >= 0) _selectTab?.Invoke(step.TabIndex);
        try { step.OnEnter?.Invoke(); } catch { /* a bad OnEnter must never break the tour */ }
        StepShown?.Invoke(this, step);
    }

    private void RaiseStep()
    {
        OnChanged(nameof(StepIndex));
        OnChanged(nameof(CurrentStep));
        OnChanged(nameof(Progress));
        OnChanged(nameof(IsFirstStep));
        OnChanged(nameof(IsLastStep));
        CommandManager.InvalidateRequerySuggested();
    }

    // ───────────────────────── legend ─────────────────────────

    public ObservableCollection<LegendItem> LegendItems { get; } = new();

    private bool _isLegendOpen;
    public bool IsLegendOpen
    {
        get => _isLegendOpen;
        set { if (_isLegendOpen == value) return; _isLegendOpen = value; OnChanged(); }
    }

    public ICommand ToggleLegendCommand { get; }

    // ───────────────────────── INPC ─────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
