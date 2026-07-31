using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Player.Services;

namespace QuizBuilder.Player.ViewModels;

/// <summary>One quiz in the library list, formatted for display.</summary>
public sealed class LibraryRow
{
    public LibraryRow(LibraryEntry entry)
    {
        Entry = entry;
        Title = string.IsNullOrWhiteSpace(entry.Title) ? "Untitled quiz" : entry.Title;
        Subtitle = $"{entry.QuestionCount} question{(entry.QuestionCount == 1 ? "" : "s")}  \u00b7  " +
                   $"added {entry.AddedAt.LocalDateTime.ToString("d MMM yyyy", CultureInfo.CurrentCulture)}";
    }

    public LibraryEntry Entry { get; }
    public Guid QuizId => Entry.QuizId;
    public string Title { get; }
    public string Subtitle { get; }
}

/// <summary>
/// The library: the taker's kept quizzes, and the entry point for opening or
/// importing one. Chosen quiz -> loaded into the session -> the existing quiz
/// screen (home) which is unchanged. Import files the quiz in the library and
/// opens it. Delete asks whether to also wipe that quiz's history and paused
/// data, since the taker decides that each time.
/// </summary>
public partial class LibraryViewModel : ObservableObject
{
    private readonly QuizSessionService _session;
    private readonly QuizLibraryService _library;
    private readonly IQbxImporter _importer;
    private readonly IAttemptHistoryService _history;
    private readonly IPausedAttemptService _paused;

    public LibraryViewModel(
        QuizSessionService session,
        QuizLibraryService library,
        IQbxImporter importer,
        IAttemptHistoryService history,
        IPausedAttemptService paused)
    {
        _session = session;
        _library = library;
        _importer = importer;
        _history = history;
        _paused = paused;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private ObservableCollection<LibraryRow> _quizzes = new();

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;

    public bool IsEmpty => Quizzes.Count == 0;

    /// <summary>Rebuilt on each appearance so an import or delete elsewhere shows.</summary>
    public void Refresh()
    {
        Quizzes = new ObservableCollection<LibraryRow>(
            _library.Entries.Select(e => new LibraryRow(e)));
        OnPropertyChanged(nameof(IsEmpty));
    }

    [RelayCommand]
    private async Task OpenAsync(LibraryRow? row)
    {
        if (row is null || IsBusy) return;

        IsBusy = true;
        StatusMessage = "Opening quiz…";
        try
        {
            var path = _library.FilePathFor(row.QuizId);
            var outcome = await _importer.LoadFromLibraryAsync(path);
            if (!outcome.Success || outcome.Package is null || outcome.Result is null)
            {
                StatusMessage = outcome.ErrorMessage ?? "That quiz couldn't be opened.";
                return;
            }

            _session.SetLoadedQuiz(outcome.Package, outcome.Result);
            StatusMessage = null;
            await Shell.Current.GoToAsync("home");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PickFileAsync()
    {
        // A .qbx has no registered device MIME type, so accept any file and let
        // Core's loader reject non-.qbx content with its clear message.
        try
        {
            var picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choose a .qbx quiz file",
            });

            if (picked is null) return; // cancelled

            await ImportUriAsync(picked.FullPath ?? picked.FileName);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't open the file picker. {ex.Message}";
        }
    }

    /// <summary>The view supplies the picked file URI (picking is a view job).</summary>
    public async Task ImportUriAsync(string uri)
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = "Opening quiz…";
        try
        {
            var outcome = await _importer.ImportFromUriAsync(uri);
            if (!outcome.Success || outcome.Package is null || outcome.Result is null)
            {
                StatusMessage = outcome.ErrorMessage ?? "That file couldn't be opened.";
                return;
            }

            // Import already filed it in the library; make it the current quiz
            // and go straight to it, since importing implies wanting to use it.
            _session.SetLoadedQuiz(outcome.Package, outcome.Result);
            Refresh();
            StatusMessage = null;
            await Shell.Current.GoToAsync("home");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(LibraryRow? row)
    {
        if (row is null) return;

        // Three-way: delete the quiz keeping its data, delete quiz AND its data,
        // or cancel. The taker decides about data each time (their choice).
        const string keepData = "Delete quiz, keep results";
        const string wipeData = "Delete quiz and results";

        var choice = await Shell.Current.DisplayActionSheetAsync(
            $"Delete \u201c{row.Title}\u201d?",
            "Cancel",
            null,
            keepData,
            wipeData);

        if (choice != keepData && choice != wipeData) return;

        _library.Remove(row.QuizId);

        if (choice == wipeData)
        {
            // Forget every attempt and paused sitting for this quiz, for everyone
            // on the device -- deleting the quiz outright is a device-level act,
            // not a per-identity one.
            _history.ClearForQuiz(row.QuizId);
            _paused.ClearForQuiz(row.QuizId);
        }

        Refresh();
    }
}
