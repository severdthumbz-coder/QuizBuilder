using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuizBuilder.Player.Services;

namespace QuizBuilder.Player.ViewModels;

/// <summary>
/// The hub after identity: import a .qbx (from the file picker or an "open with"
/// intent), see what was loaded, and start taking it. Also the screen an
/// incoming-file intent lands on.
/// </summary>
public partial class HomeViewModel : ObservableObject
{
    private readonly QuizSessionService _session;
    private readonly IQbxImporter _importer;

    public HomeViewModel(QuizSessionService session, IQbxImporter importer)
    {
        _session = session;
        _importer = importer;

        // Reflect anything already loaded (e.g. returning from a completed take).
        RefreshFromSession();
    }

    /// <summary>
    /// Called when the home page appears. Subscribes to OS file-open events and
    /// drains any file that arrived during a cold start before this screen was
    /// listening. Paired with <see cref="Detach"/> so exactly one live home VM
    /// is ever subscribed.
    /// </summary>
    public void Attach()
    {
        IncomingFileHandler.FileOffered -= OnFileOffered; // idempotent
        IncomingFileHandler.FileOffered += OnFileOffered;

        RefreshFromSession();

        var pending = IncomingFileHandler.TakePending();
        if (pending is not null)
            _ = ImportUriAsync(pending);
    }

    public void Detach() => IncomingFileHandler.FileOffered -= OnFileOffered;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasQuiz))]
    private string? _quizTitle;

    [ObservableProperty] private string? _quizDescription;

    [ObservableProperty] private int _questionCount;

    [ObservableProperty] private string? _warningText;

    public bool HasQuiz => !string.IsNullOrEmpty(QuizTitle);

    public string WelcomeLine =>
        _session.Identity is { } id ? $"Signed in as {id.FullName}" : string.Empty;

    private void RefreshFromSession()
    {
        if (_session.Loaded is { } loaded)
        {
            QuizTitle = loaded.Document.Title;
            QuizDescription = string.IsNullOrWhiteSpace(loaded.Document.Description)
                ? null : loaded.Document.Description;
            QuestionCount = loaded.Document.QuestionCount;
            WarningText = loaded.Warnings.Count > 0
                ? string.Join("\n", loaded.Warnings)
                : null;
        }
    }

    [RelayCommand]
    private async Task PickFileAsync()
    {
        // A .qbx has no registered device MIME type, so accept any file and let
        // Core's loader reject non-.qbx content with its clear message. On
        // Android the picker returns a content:// URI, handled by the importer.
        try
        {
            var picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choose a .qbx quiz file",
            });

            if (picked is null) return; // user cancelled

            await ImportUriAsync(picked.FullPath ?? picked.FileName);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't open the file picker. {ex.Message}";
        }
    }

    private void OnFileOffered(string uri) => _ = ImportUriAsync(uri);

    private async Task ImportUriAsync(string uri)
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = "Opening quiz…";
        WarningText = null;

        try
        {
            var outcome = await _importer.ImportFromUriAsync(uri);
            if (!outcome.Success || outcome.Package is null || outcome.Result is null)
            {
                StatusMessage = outcome.ErrorMessage ?? "That file couldn't be opened.";
                return;
            }

            _session.SetLoadedQuiz(outcome.Package, outcome.Result);
            RefreshFromSession();
            StatusMessage = null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // NOTE: deliberately NOT gated with [RelayCommand(CanExecute=...)]. A
    // computed CanExecute that depends on QuestionCount is prone to a stale
    // state: the command caches its CanExecute result and only refreshes when
    // NotifyCanExecuteChanged fires, and the initial evaluation can run while
    // QuestionCount is still 0 (before import completes), leaving the button
    // permanently disabled even after a quiz loads. Validating inside the
    // command instead is immune to that timing and gives the user a reason
    // when nothing happens.
    [RelayCommand]
    private async Task StartAsync()
    {
        if (!HasQuiz)
        {
            StatusMessage = "Load a quiz first.";
            return;
        }
        if (QuestionCount <= 0)
        {
            StatusMessage = "This quiz has no questions to take.";
            return;
        }

        _session.StartTake();
        await Shell.Current.GoToAsync("take");
    }
}
