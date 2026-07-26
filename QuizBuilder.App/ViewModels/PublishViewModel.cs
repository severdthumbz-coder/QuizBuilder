using System.IO;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;

namespace QuizBuilder.App.ViewModels;

/// <summary>
/// The Publish tab.
///
/// HTML only, deliberately. PDF is reachable from here via the browser's own
/// print engine, which paginates better than a hand-rolled layout and carries
/// no licence obligation -- the good .NET PDF libraries are variously revenue-
/// gated (QuestPDF) or viral (iText AGPL), and the MIT ones would mean writing
/// page layout blind. Word and Excel follow one at a time, each proven before
/// the next, because an unverifiable NuGet dependency is exactly the kind of
/// scaffolding that has broken every previous slice.
/// </summary>
public sealed class PublishViewModel : ViewModelBase
{
    private readonly IQuizDocumentService _document;
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly IQuizCompiler _compiler;
    private readonly IHtmlExporter _html;
    private readonly IQuizWebExporter _web;
    private readonly IWordExporter _word;
    private readonly IQuizPackageService _package;
    private readonly IExcelExporter _excel;
    private readonly IExcelImporter _excelImport;

    private bool _includeAnswerKey;
    private bool _includePrintButton = true;
    private string _statusMessage = string.Empty;
    private string? _lastExportPath;

    public PublishViewModel(
        IQuizDocumentService document,
        ISettingsService settings,
        IThemeService theme,
        IQuizCompiler compiler,
        IHtmlExporter html,
        IQuizWebExporter web,
        IWordExporter word,
        IExcelExporter excel,
        IExcelImporter excelImport,
        IQuizPackageService package)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _compiler = compiler ?? throw new ArgumentNullException(nameof(compiler));
        _html = html ?? throw new ArgumentNullException(nameof(html));
        _web = web ?? throw new ArgumentNullException(nameof(web));
        _word = word ?? throw new ArgumentNullException(nameof(word));
        _package = package ?? throw new ArgumentNullException(nameof(package));
        _excel = excel ?? throw new ArgumentNullException(nameof(excel));
        _excelImport = excelImport ?? throw new ArgumentNullException(nameof(excelImport));

        // Same reasoning as the Preview tab: this ViewModel stays alive and
        // subscribed while the user types elsewhere, and RefreshSummary calls
        // Compile(). Cheap on its own, but not free per keystroke -- and it is
        // pure waste when the tab is not on screen. OnActivated refreshes on the
        // way in.
        _document.DocumentChanged += (_, _) => RefreshOrDefer();
        _settings.SettingsChanged += (_, _) => RefreshOrDefer();

        RefreshSummary();
    }

    public bool IncludeAnswerKey
    {
        get => _includeAnswerKey;
        set
        {
            if (SetProperty(ref _includeAnswerKey, value)) OnPropertyChanged(nameof(ExportDescription));
        }
    }

    public bool IncludePrintButton
    {
        get => _includePrintButton;
        set => SetProperty(ref _includePrintButton, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? LastExportPath
    {
        get => _lastExportPath;
        private set
        {
            if (SetProperty(ref _lastExportPath, value)) OnPropertyChanged(nameof(HasExported));
        }
    }

    public bool HasExported => !string.IsNullOrEmpty(_lastExportPath);

    public string ExportDescription => _includeAnswerKey
        ? "The page will include the answers, marked with a tick."
        : "The page will show the paper as a student sees it, with no answers.";

    private string _summary = string.Empty;
    public string Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public bool CanExport { get; private set; }

    /// <summary>
    /// Called when the tab becomes visible. Settings and the theme live on other
    /// tabs, so without this the summary could describe a paper compiled under
    /// settings that have since changed.
    /// </summary>
    public void OnActivated()
    {
        _isVisible = true;

        if (_isStale) RefreshSummary();
    }

    /// <summary>Called when the tab is hidden, so refreshes can be deferred.</summary>
    public void OnDeactivated() => _isVisible = false;

    private void RefreshOrDefer()
    {
        if (_isVisible)
        {
            RefreshSummary();
            return;
        }

        _isStale = true;
    }

    private void RefreshSummary()
    {
        _isStale = false;

        var compiled = _compiler.Compile(_document.Current, _settings.Current.Quiz, seed: 0);

        CanExport = compiled.QuestionCount > 0;
        OnPropertyChanged(nameof(CanExport));

        Summary = compiled.QuestionCount == 0
            ? "Add some questions in the Quiz Builder tab before exporting."
            : $"{compiled.QuestionCount} question{(compiled.QuestionCount == 1 ? "" : "s")}"
              + $"  ·  {compiled.TotalPoints:0.##} point{(compiled.TotalPoints == 1 ? "" : "s")}"
              + $"  ·  theme: {_theme.Current.DisplayName}";

        RelayCommand.RaiseCanExecuteChanged();
    }

    /// <summary>
    /// Renders to the given path. The view supplies it from a file dialog:
    /// opening one here would put a WPF dependency in the ViewModel and make
    /// this untestable.
    /// </summary>
    public async Task<bool> ExportHtmlAsync(string path)
    {
        try
        {
            // Seed 0 rather than a random one: an export should be reproducible.
            // Randomising here would mean two exports of the same quiz silently
            // differ, and the teacher printing a class set would not know which
            // paper they were holding.
            var compiled = _compiler.Compile(_document.Current, _settings.Current.Quiz, seed: 0);

            var html = _html.Render(compiled, _theme.Current, new HtmlExportOptions
            {
                ShowAnswers = _includeAnswerKey,
                IncludePrintButton = _includePrintButton,
            });

            await File.WriteAllTextAsync(path, html);

            LastExportPath = path;
            StatusMessage = $"Exported to {Path.GetFileName(path)}.";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not export: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Exports a self-grading HTML file: the quiz plus a browser grader that
    /// mirrors the in-app one.
    ///
    /// The pass mark comes from the quiz settings, so the browser judges pass or
    /// fail exactly as the app does -- the two graders were checked to agree on a
    /// battery covering every rule and the pass boundary.
    /// </summary>
    public async Task<bool> ExportWebAsync(string path)
    {
        try
        {
            // Seed 0, like the other exports: a self-grading page is a
            // deliverable, and two exports of the same quiz should be identical.
            var compiled = _compiler.Compile(_document.Current, _settings.Current.Quiz, seed: 0);

            var html = _web.Render(compiled, _theme.Current, new WebExportOptions
            {
                PassPercentage = _settings.Current.Quiz.PassPercentage,
                PassOnQuestionCount = _settings.Current.Quiz.PassMarkBasis == PassMarkBasis.QuestionCount,
                TimeLimitMinutes = _settings.Current.Quiz.TimeLimitMinutes,
            });

            await File.WriteAllTextAsync(path, html);

            LastExportPath = path;
            StatusMessage = $"Exported self-grading quiz to {Path.GetFileName(path)}.";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not export: {ex.Message}";
            return false;
        }
    }

    public async Task<bool> ExportWordAsync(string path)
    {
        try
        {
            var compiled = _compiler.Compile(_document.Current, _settings.Current.Quiz, seed: 0);

            // Build in memory, then write once. Writing straight to the file
            // would leave a half-formed .docx behind if anything threw
            // partway -- and a corrupt file that exists is worse than no file,
            // because Word's error message says nothing useful.
            using var buffer = new MemoryStream();
            _word.Write(buffer, compiled, _theme.Current, new WordExportOptions
            {
                ShowAnswers = _includeAnswerKey,
                ImageBytesResolver = _package.GetImage,
            });

            await File.WriteAllBytesAsync(path, buffer.ToArray());

            LastExportPath = path;
            StatusMessage = $"Exported to {Path.GetFileName(path)}.";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not export: {ex.Message}";
            return false;
        }
    }

    public async Task<bool> ExportExcelAsync(string path)
    {
        try
        {
            // The AUTHORED document, not a compiled paper: this sheet exists to
            // be edited and read back, so shuffling or dropping questions to a
            // selection count would produce a file that silently disagrees with
            // the quiz it came from.
            using var buffer = new MemoryStream();
            _excel.Write(buffer, _document.Current);

            await File.WriteAllBytesAsync(path, buffer.ToArray());

            LastExportPath = path;
            StatusMessage = $"Exported to {Path.GetFileName(path)}.";
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not export: {ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Reads a spreadsheet and REPLACES the current quiz.
    ///
    /// The caller must confirm first when <see cref="IQuizDocumentService.IsDirty"/>
    /// -- this throws the current document away, and doing that silently to
    /// someone's unsaved work is unforgivable. The view owns that prompt because
    /// a ViewModel that opens dialogs cannot be tested.
    /// </summary>
    public async Task<ImportResult> ImportExcelAsync(string path)
    {
        try
        {
            // Read fully first, then touch the document. If the file is
            // unreadable the current quiz must be exactly as it was -- a
            // half-applied import would be worse than no import.
            var bytes = await File.ReadAllBytesAsync(path);

            using var buffer = new MemoryStream(bytes);
            var result = _excelImport.Read(buffer);

            if (!result.Success)
            {
                StatusMessage = result.Error ?? "Could not read that file.";
                return result;
            }

            // The sheet holds questions, not a title -- keep the current one so
            // an import does not silently rename the quiz to nothing.
            result.Document!.Title = _document.Current.Title;
            result.Document.Description = _document.Current.Description;

            _document.LoadDocument(result.Document, filePath: null);

            StatusMessage = result.Problems.Count == 0
                ? $"Imported {result.QuestionCount} question{(result.QuestionCount == 1 ? "" : "s")}."
                : $"Imported {result.QuestionCount} question{(result.QuestionCount == 1 ? "" : "s")}, "
                  + $"with {result.Problems.Count} note{(result.Problems.Count == 1 ? "" : "s")}.";

            return result;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not import: {ex.Message}";
            return ImportResult.Failed(ex.Message);
        }
    }

    /// <summary>True when replacing the document would lose unsaved work.</summary>
    /// <summary>Whether this tab is on screen. See RefreshOrDefer.</summary>
    private bool _isVisible;

    /// <summary>Set when a change arrived while hidden. Cleared by RefreshSummary.</summary>
    private bool _isStale = true;

    public bool ImportWouldDiscardChanges => _document.IsDirty;

    /// <summary>Suggested filename, stripped of characters Windows rejects.</summary>
    public string SuggestFileName(string extension = ".html", string? label = null)
    {
        var title = _document.Current.Title;

        // The answer-key suffix applies to the printable page. A caller can pass
        // its own label instead (the self-grading quiz passes " quiz"), so the
        // filename says what the file is.
        var suffix = label ?? (_includeAnswerKey ? " answer key" : string.Empty);

        if (string.IsNullOrWhiteSpace(title)) return $"quiz{suffix}{extension}";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Where(c => !invalid.Contains(c)).ToArray()).Trim();

        return string.IsNullOrEmpty(cleaned) ? $"quiz{suffix}{extension}" : $"{cleaned}{suffix}{extension}";
    }
}
