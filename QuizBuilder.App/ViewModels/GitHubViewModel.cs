using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.App.ViewModels;

/// <summary>
/// The GitHub tab: connect an account, publish the quiz as a web page, get a
/// link to hand out.
///
/// The token never lives in a bound property. It is read from the password box
/// at the moment it is used and passed straight to the service, so it is not
/// sitting in the ViewModel's state where a binding, a debugger dump or a crash
/// report could pick it up. That is why the view calls these methods with the
/// token as an argument rather than binding to it.
/// </summary>
public sealed class GitHubViewModel : ViewModelBase
{
    private readonly IGitHubService _gitHub;
    private readonly ISettingsService _settings;
    private readonly IQuizDocumentService _document;
    private readonly IHtmlExporter _html;
    private readonly IQuizCompiler _compiler;
    private readonly IThemeService _theme;

    private string _repositoryText = string.Empty;
    private string _branch = "main";
    private string _fileName = "index.html";
    private string _status = string.Empty;
    private bool _isBusy;
    private string? _lastPublishedUrl;

    public GitHubViewModel(
        IGitHubService gitHub,
        ISettingsService settings,
        IQuizDocumentService document,
        IHtmlExporter html,
        IQuizCompiler compiler,
        IThemeService theme)
    {
        _gitHub = gitHub ?? throw new ArgumentNullException(nameof(gitHub));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _html = html ?? throw new ArgumentNullException(nameof(html));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));

        var stored = _settings.Current.GitHub;

        _repositoryText = stored.RepositoryUrl ?? string.Empty;
        _branch = string.IsNullOrWhiteSpace(stored.DefaultBranch) ? "main" : stored.DefaultBranch;
        _lastPublishedUrl = stored.PublishedPagesUrl;

        OpenPublishedCommand = new RelayCommand(
            () => { /* handled by the view: opening a browser is not a ViewModel's job */ },
            () => !string.IsNullOrWhiteSpace(_lastPublishedUrl));
    }

    public RelayCommand OpenPublishedCommand { get; }

    public string RepositoryText
    {
        get => _repositoryText;
        set
        {
            if (_repositoryText == value) return;

            _repositoryText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RepositoryHint));
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// Live feedback on what was typed. Deliberately not an error style until
    /// there is something to say: a red box on an empty field the user has not
    /// filled in yet is nagging, not helping.
    /// </summary>
    public string RepositoryHint
    {
        get
        {
            if (string.IsNullOrWhiteSpace(_repositoryText)) return string.Empty;

            var repository = RepositoryReference.TryParse(_repositoryText, out var error);

            return repository is null ? error ?? string.Empty : $"Will publish to {repository.FullName}.";
        }
    }

    public bool RepositoryIsValid => RepositoryReference.TryParse(_repositoryText, out _) is not null;

    public string Branch
    {
        get => _branch;
        set
        {
            if (_branch == value) return;

            _branch = value;
            OnPropertyChanged();
        }
    }

    public string FileName
    {
        get => _fileName;
        set
        {
            if (_fileName == value) return;

            _fileName = value;
            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;

            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    /// <summary>
    /// Drives the status line's visibility. A bool, because
    /// BoolToVisibilityConverter converts bools -- handing it a string would
    /// depend on whatever its default branch happens to do.
    /// </summary>
    public bool HasStatus => !string.IsNullOrWhiteSpace(_status);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value) return;

            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotBusy));
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsNotBusy => !_isBusy;

    public string? LastPublishedUrl
    {
        get => _lastPublishedUrl;
        private set
        {
            if (_lastPublishedUrl == value) return;

            _lastPublishedUrl = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPublishedUrl));
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasPublishedUrl => !string.IsNullOrWhiteSpace(_lastPublishedUrl);

    /// <summary>True when a token is stored and readable.</summary>
    public bool HasStoredToken => !string.IsNullOrWhiteSpace(SafeGetToken());

    /// <summary>True when a stored token needs a passphrase before it can be used.</summary>
    public bool RequiresPassphrase => _settings.RequiresPassphrase;

    // --- Actions ------------------------------------------------------------

    /// <summary>
    /// Checks a token and, if it works, stores it under the user's chosen
    /// protection mode.
    /// </summary>
    public async Task ConnectAsync(string token)
    {
        if (IsBusy) return;

        IsBusy = true;
        Status = "Checking that token...";

        try
        {
            var result = await _gitHub.VerifyTokenAsync(token);

            Status = result.Message ?? string.Empty;

            if (!result.Success) return;

            try
            {
                _settings.SetGitHubToken(token);
                _settings.Save();
            }
            catch (InvalidOperationException ex)
            {
                // Passphrase mode, still locked. The token is good; we just
                // cannot store it yet. Say exactly that rather than implying the
                // token failed.
                Status = $"{result.Message} It could not be saved: {ex.Message}";
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Forgets the stored token.</summary>
    public void Disconnect()
    {
        _settings.SetGitHubToken(null);
        _settings.Save();

        Status = "Token removed from this machine.";

        OnPropertyChanged(nameof(HasStoredToken));
    }

    /// <summary>
    /// Renders the quiz to HTML and publishes it, then turns on Pages.
    ///
    /// <paramref name="token"/> is passed in rather than held: see the class
    /// note.
    /// </summary>
    public async Task PublishAsync(string? token)
    {
        if (IsBusy) return;

        var actualToken = string.IsNullOrWhiteSpace(token) ? SafeGetToken() : token;

        if (string.IsNullOrWhiteSpace(actualToken))
        {
            Status = "Connect a GitHub account first.";
            return;
        }

        var repository = RepositoryReference.TryParse(_repositoryText, out var repositoryError);
        if (repository is null)
        {
            Status = repositoryError ?? "Enter a repository first.";
            return;
        }

        var compiled = _compiler.Compile(_document.Current, _settings.Current.Quiz, seed: 0);
        if (compiled.QuestionCount == 0)
        {
            Status = "There are no questions to publish yet.";
            return;
        }

        IsBusy = true;

        try
        {
            Status = "Building the page...";

            // Seed 0 and no answers: a published page is the student copy, and a
            // reproducible one -- republishing an unchanged quiz should produce
            // an identical file rather than a spurious commit.
            var html = _html.Render(compiled, _theme.Current, new HtmlExportOptions
            {
                ShowAnswers = false,

                // Off: this page is destined for a website, and HtmlExportOptions
                // says a print bar is noise there. The Publish tab's file export
                // is the one people print.
                IncludePrintButton = false,
            });

            Status = $"Publishing to {repository.FullName}...";

            var publish = await _gitHub.PublishFileAsync(
                actualToken!,
                repository,
                _branch,
                _fileName,
                html,
                $"Publish {_document.Current.Title}");

            if (!publish.Success)
            {
                Status = publish.Message ?? "Could not publish.";
                return;
            }

            Status = "Turning on GitHub Pages...";

            var pages = await _gitHub.EnablePagesAsync(actualToken!, repository, _branch);

            // Remember what worked, so the next session starts where this one
            // left off.
            var stored = _settings.Current.GitHub;
            stored.RepositoryUrl = repository.FullName;
            stored.DefaultBranch = _branch;

            if (pages.Success && !string.IsNullOrWhiteSpace(pages.Url))
            {
                LastPublishedUrl = PageUrlFor(pages.Url!, _fileName);
                stored.PublishedPagesUrl = LastPublishedUrl;

                Status = $"{publish.Message} {pages.Message}";
            }
            else
            {
                // The file IS published even when Pages could not be turned on --
                // often because the token lacks the pages scope. Saying "failed"
                // here would be a lie and would send the user hunting for a
                // problem that is not there.
                LastPublishedUrl = publish.Url;

                Status = $"{publish.Message} The file is published, but Pages could not be enabled: {pages.Message}";
            }

            _settings.Save();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // --- Internals ----------------------------------------------------------

    /// <summary>
    /// Joins the Pages site URL to the published file.
    ///
    /// index.html is special: a Pages site serves it at the directory root, so
    /// appending the name would give a working but ugly link.
    /// </summary>
    private static string PageUrlFor(string siteUrl, string fileName)
    {
        var trimmedSite = siteUrl.TrimEnd('/');
        var trimmedFile = fileName.Trim().TrimStart('/');

        if (string.IsNullOrEmpty(trimmedFile)
            || string.Equals(trimmedFile, "index.html", StringComparison.OrdinalIgnoreCase))
        {
            return trimmedSite + "/";
        }

        return $"{trimmedSite}/{trimmedFile}";
    }

    /// <summary>
    /// Reads the stored token without letting a locked or foreign blob take the
    /// tab down. Null means "ask the user", never "crash".
    /// </summary>
    private string? SafeGetToken()
    {
        try
        {
            return _settings.GetGitHubToken();
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void OnActivated()
    {
        OnPropertyChanged(nameof(HasStoredToken));
        OnPropertyChanged(nameof(RequiresPassphrase));
        OnPropertyChanged(nameof(RepositoryHint));

        RelayCommand.RaiseCanExecuteChanged();
    }
}
