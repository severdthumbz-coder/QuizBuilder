using System.Collections.ObjectModel;
using QuizBuilder.App.Services;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;

namespace QuizBuilder.App.ViewModels;

/// <summary>One flagged occurrence as a row in the review panel.</summary>
public sealed class SpellIssueRowViewModel : ViewModelBase
{
    private readonly Action _onResolved;

    public SpellIssueRowViewModel(
        TextField field, int start, int length, string word,
        IReadOnlyList<string> suggestions, string context, Action onResolved)
    {
        Field = field;
        Start = start;
        Length = length;
        Word = word;
        Suggestions = new ObservableCollection<string>(suggestions);
        Context = context;
        _onResolved = onResolved;

        SelectedSuggestion = Suggestions.FirstOrDefault();
    }

    public TextField Field { get; }
    public int Start { get; }
    public int Length { get; }
    public string Word { get; }

    /// <summary>A short window of text around the word, so the reviewer sees it
    /// in situ rather than as a bare word.</summary>
    public string Context { get; }

    public ObservableCollection<string> Suggestions { get; }

    private string? _selectedSuggestion;
    public string? SelectedSuggestion
    {
        get => _selectedSuggestion;
        set => SetProperty(ref _selectedSuggestion, value);
    }

    /// <summary>The field label plus location, e.g. "Question prompt".</summary>
    public string Where => Field.Label;

    private bool _isResolved;
    public bool IsResolved
    {
        get => _isResolved;
        set => SetProperty(ref _isResolved, value);
    }

    public bool HasSuggestions => Suggestions.Count > 0;

    internal Action OnResolved => _onResolved;
}

/// <summary>Issues grouped under one section heading (or the quiz-level group).</summary>
public sealed class SpellSectionGroupViewModel
{
    public SpellSectionGroupViewModel(string heading, IReadOnlyList<SpellIssueRowViewModel> issues)
    {
        Heading = heading;
        Issues = new ObservableCollection<SpellIssueRowViewModel>(issues);
    }

    public string Heading { get; }
    public ObservableCollection<SpellIssueRowViewModel> Issues { get; }
    public int Count => Issues.Count;
}

/// <summary>
/// Drives the spell-check review dialog. Runs the offline
/// <see cref="ITextReviewProvider"/> over the current document's text inventory,
/// presents the issues grouped by section (the primary UX requirement), and
/// applies Replace / Ignore / Add-to-dictionary. Replace routes through
/// <see cref="SpellFixApplier"/> so corrections land in undo + dirty-tracking;
/// after any model-mutating action the review is re-run, because undo replaces
/// the whole document and stale <see cref="TextField"/> closures must not be
/// reused.
/// </summary>
public sealed class SpellCheckViewModel : ViewModelBase
{
    private readonly IQuizDocumentService _document;
    private readonly ITextReviewProvider _provider;
    private readonly SpellIgnoreListStore _ignoreList;
    private readonly SpellFixApplier _applier;

    public SpellCheckViewModel(
        IQuizDocumentService document,
        ITextReviewProvider provider,
        SpellIgnoreListStore ignoreList,
        SpellFixApplier applier)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _ignoreList = ignoreList ?? throw new ArgumentNullException(nameof(ignoreList));
        _applier = applier ?? throw new ArgumentNullException(nameof(applier));

        Run();
    }

    public ObservableCollection<SpellSectionGroupViewModel> Groups { get; } = new();

    private int _issueCount;
    public int IssueCount
    {
        get => _issueCount;
        private set
        {
            if (SetProperty(ref _issueCount, value))
            {
                OnPropertyChanged(nameof(HasIssues));
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public bool HasIssues => _issueCount > 0;

    public string SummaryText => _issueCount == 0
        ? "No spelling issues found."
        : _issueCount == 1
            ? "1 possible spelling issue."
            : $"{_issueCount} possible spelling issues.";

    /// <summary>
    /// (Re-)runs the review against the CURRENT document and rebuilds the
    /// grouped view. Called on open and after every Replace/Ignore/Add, so the
    /// panel never holds closures over a document that undo may have replaced.
    /// </summary>
    public void Run()
    {
        var document = _document.Current;
        var fields = DocumentTextInventory.Enumerate(document);
        var issues = _provider.Review(fields);

        // Map each section id to its title, for group headings, plus a synthetic
        // group for quiz-level text (title/description/study cards).
        var sectionTitles = document.Sections.ToDictionary(
            s => s.Id,
            s => string.IsNullOrWhiteSpace(s.Title) ? "(untitled section)" : s.Title);

        // Preserve document order: quiz-level first, then sections in order.
        var order = new List<Guid?> { null };
        order.AddRange(document.Sections.Select(s => (Guid?)s.Id));

        var rowsByGroup = new Dictionary<Guid?, List<SpellIssueRowViewModel>>();

        foreach (var issue in issues)
        {
            foreach (var occ in issue.Occurrences)
            {
                var groupKey = occ.Field.SectionId; // null => quiz-level group
                if (!rowsByGroup.TryGetValue(groupKey, out var list))
                {
                    list = new List<SpellIssueRowViewModel>();
                    rowsByGroup[groupKey] = list;
                }

                list.Add(new SpellIssueRowViewModel(
                    occ.Field, occ.Start, occ.Length, issue.Word,
                    issue.Suggestions, BuildContext(occ.Field.Text, occ.Start, occ.Length),
                    onResolved: Run));
            }
        }

        Groups.Clear();
        int total = 0;
        foreach (var key in order)
        {
            if (!rowsByGroup.TryGetValue(key, out var rows) || rows.Count == 0)
                continue;

            var heading = key is null
                ? "Quiz (title, description, study cards)"
                : sectionTitles.TryGetValue(key.Value, out var t) ? t : "(section)";

            Groups.Add(new SpellSectionGroupViewModel(heading, rows));
            total += rows.Count;
        }

        IssueCount = total;
    }

    /// <summary>Replaces this occurrence with its selected suggestion, then
    /// re-runs the review.</summary>
    public void Replace(SpellIssueRowViewModel row)
    {
        if (row is null || string.IsNullOrEmpty(row.SelectedSuggestion))
            return;

        try
        {
            _applier.Apply(row.Field, row.Start, row.Length, row.SelectedSuggestion!);
        }
        catch (InvalidOperationException)
        {
            // Offsets went stale (a prior fix in the same field shifted them);
            // re-running realigns everything.
        }

        Run();
    }

    /// <summary>Adds the word to the custom dictionary and re-runs so every
    /// occurrence of it disappears.</summary>
    public void Ignore(SpellIssueRowViewModel row)
    {
        if (row is null) return;
        _ignoreList.Add(row.Word);
        Run();
    }

    private static string BuildContext(string text, int start, int length)
    {
        const int pad = 24;
        int from = Math.Max(0, start - pad);
        int to = Math.Min(text.Length, start + length + pad);
        var slice = text.Substring(from, to - from).Replace('\n', ' ').Replace('\r', ' ');
        var prefix = from > 0 ? "…" : string.Empty;
        var suffix = to < text.Length ? "…" : string.Empty;
        return prefix + slice + suffix;
    }
}
