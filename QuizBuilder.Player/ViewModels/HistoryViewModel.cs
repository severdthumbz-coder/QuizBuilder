using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Player.Services;

namespace QuizBuilder.Player.ViewModels;

/// <summary>
/// One row in the history list: a past sitting, formatted for display. The raw
/// <see cref="AttemptRecord"/> is kept so tapping the row can open its detail
/// without a second lookup.
/// </summary>
public sealed class AttemptRow
{
    public AttemptRow(AttemptRecord record)
    {
        Record = record;

        // Local time, because a taker reads history in the timezone they took
        // it in; the record stores an absolute DateTimeOffset.
        TakenAt = record.TakenAt.LocalDateTime
            .ToString("d MMM yyyy, h:mm tt", CultureInfo.CurrentCulture);

        if (record.Percentage is { } pct)
        {
            Score = $"{pct.ToString("0.#", CultureInfo.CurrentCulture)}%";
            HasPassFail = record.Passed is not null;
            IsPass = record.Passed == true;
            IsFail = record.Passed == false;
            PassFail = record.Passed switch { true => "PASS", false => "FAIL", _ => string.Empty };
        }
        else
        {
            // An all-essay paper has no auto score.
            Score = "Pending review";
            HasPassFail = false;
        }

        ReviewNote = record.QuestionsAwaitingReview > 0
            ? $"{record.QuestionsAwaitingReview} awaiting review"
            : string.Empty;
    }

    public AttemptRecord Record { get; }

    public Guid Id => Record.Id;
    public string TakenAt { get; }
    public string Score { get; }
    public bool HasPassFail { get; }
    public bool IsPass { get; }
    public bool IsFail { get; }
    public string PassFail { get; }
    public string ReviewNote { get; }
    public bool HasReviewNote => ReviewNote.Length > 0;
}

/// <summary>
/// This quiz's past sittings, newest first, with a way into each one's detail
/// and a way to forget them. Reads straight from the injected history service
/// (the same singleton the session appends to on submit) and refreshes on its
/// HistoryChanged event, so deleting a row updates the list without a reload.
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly QuizSessionService _session;
    private readonly IAttemptHistoryService _history;

    public HistoryViewModel(QuizSessionService session, IAttemptHistoryService history)
    {
        _session = session;
        _history = history;

        QuizTitle = _session.QuizTitle;
        Reload();
    }

    /// <summary>
    /// Called from the page's OnAppearing. Subscribes to the singleton history
    /// service and refreshes -- so returning from an attempt's detail, or a
    /// delete made elsewhere, is reflected. Paired with <see cref="Detach"/> on
    /// OnDisappearing so exactly one live subscription exists per visit, which
    /// matters because the service outlives this transient VM.
    /// </summary>
    public void Attach()
    {
        _history.HistoryChanged -= OnHistoryChanged; // idempotent
        _history.HistoryChanged += OnHistoryChanged;
        Reload();
    }

    public void Detach() => _history.HistoryChanged -= OnHistoryChanged;

    [ObservableProperty] private string _quizTitle = "Quiz";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private ObservableCollection<AttemptRow> _attempts = new();

    public bool IsEmpty => Attempts.Count == 0;

    private Guid QuizId => _session.Loaded?.Document.Id ?? Guid.Empty;

    private void OnHistoryChanged(object? sender, EventArgs e) => Reload();

    private void Reload()
    {
        var rows = _history.ForQuiz(QuizId).Select(r => new AttemptRow(r));
        Attempts = new ObservableCollection<AttemptRow>(rows);
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>Open one sitting's per-question detail. The record travels via a
    /// short-lived handoff on the session so the detail VM needs no navigation
    /// parameter plumbing.</summary>
    [RelayCommand]
    private async Task OpenAsync(AttemptRow? row)
    {
        if (row is null) return;

        _session.SelectedAttempt = row.Record;
        await Shell.Current.GoToAsync("attempt");
    }

    [RelayCommand]
    private async Task DeleteAsync(AttemptRow? row)
    {
        if (row is null) return;

        var confirmed = await Shell.Current.DisplayAlertAsync(
            "Delete this attempt?",
            "This removes the saved record of this sitting. It can't be undone.",
            "Delete", "Keep");

        if (!confirmed) return;

        // Remove() raises HistoryChanged, which calls Reload for us.
        _history.Remove(row.Id);
    }

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        if (IsEmpty) return;

        var confirmed = await Shell.Current.DisplayAlertAsync(
            "Clear all history?",
            $"This forgets every saved attempt for \u201c{QuizTitle}\u201d. It can't be undone.",
            "Clear all", "Keep");

        if (!confirmed) return;

        _history.ClearForQuiz(QuizId);
    }
}
