using System.Collections.ObjectModel;
using System.Windows.Input;
using QuizBuilder.App.Services;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;

namespace QuizBuilder.App.ViewModels;

/// <summary>One AI grammar suggestion as a row: the original text, the proposed
/// rewrite, the reason, and whether it can be applied in place.</summary>
public sealed class GrammarSuggestionRow : ViewModelBase
{
    public GrammarSuggestionRow(GrammarSuggestion suggestion, TextField field, bool replaceable, string where)
    {
        Suggestion = suggestion;
        Field = field;
        Replaceable = replaceable;
        Where = where;
    }

    public GrammarSuggestion Suggestion { get; }
    public TextField Field { get; }
    public bool Replaceable { get; }

    /// <summary>The field label, e.g. "Question prompt", for context.</summary>
    public string Where { get; }

    public string Original => Suggestion.Original;
    public string Rewrite => Suggestion.Rewrite;
    public string Explanation => Suggestion.Explanation;

    /// <summary>Accept is offered only when the rewrite can be spliced in place
    /// (not a description row, whose offsets are on stripped text).</summary>
    public bool CanAccept => Replaceable;
}

/// <summary>
/// Drives the AI grammar-check dialog. Lets the user pick a scope (section /
/// study cards / whole quiz), runs the async provider, and shows the returned
/// suggestions as accept/reject diff rows. Accept routes through
/// <see cref="SpellFixApplier"/> (so it lands in undo, like a spelling fix);
/// Accept all applies every remaining acceptable row in one undo batch.
///
/// <para>
/// The whole call is async and cancellable. When the provider is Off, the run
/// is short-circuited with a message pointing to Settings. Suggestions whose
/// source span has shifted (because an earlier accept in the same field changed
/// offsets) are skipped and the run can be repeated to realign — the provider
/// re-review is the clean way to continue.
/// </para>
/// </summary>
public sealed class GrammarCheckViewModel : ViewModelBase
{
    private readonly IQuizDocumentService _document;
    private readonly ISettingsService _settings;
    private readonly IGrammarReviewProvider _provider;
    private readonly SpellFixApplier _applier;
    private readonly Func<GrammarScope, Guid?, GrammarScopeSelection> _buildSelection;

    private CancellationTokenSource? _cts;

    public GrammarCheckViewModel(
        IQuizDocumentService document,
        ISettingsService settings,
        IGrammarReviewProvider provider,
        SpellFixApplier applier,
        GrammarScope initialScope,
        Guid? sectionId,
        string? sectionLabel)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _applier = applier ?? throw new ArgumentNullException(nameof(applier));

        _sectionId = sectionId;
        _buildSelection = (scope, sid) =>
            GrammarScopeBuilder.Build(DocumentTextInventory.Enumerate(_document.Current), scope, sid);

        ScopeOptions = BuildScopeOptions(sectionId, sectionLabel);
        _selectedScope = ScopeOptions.FirstOrDefault(o => o.Scope == initialScope) ?? ScopeOptions[0];

        AcceptAllCommand = new RelayCommand(AcceptAll, () => Suggestions.Any(s => s.CanAccept));
    }

    private readonly Guid? _sectionId;

    public IReadOnlyList<GrammarScopeOption> ScopeOptions { get; }

    private GrammarScopeOption _selectedScope;
    public GrammarScopeOption SelectedScope
    {
        get => _selectedScope;
        set => SetProperty(ref _selectedScope, value);
    }

    public ObservableCollection<GrammarSuggestionRow> Suggestions { get; } = new();

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(CanRun));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsIdle => !IsBusy;

    private string _statusMessage = "Pick a scope and run the check.";
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    private bool _hasError;
    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public bool HasSuggestions => Suggestions.Count > 0;

    public ICommand AcceptAllCommand { get; }

    /// <summary>Cancels an in-flight check. Safe to call when idle.</summary>
    public void Cancel() => _cts?.Cancel();

    public bool CanRun => !IsBusy;

    public async Task RunAsync()
    {
        if (_settings.Current.AiReview.Provider == AiProvider.Off)
        {
            HasError = true;
            StatusMessage = "AI grammar review is off. Turn it on in Settings → AI grammar review.";
            return;
        }

        Suggestions.Clear();
        OnPropertyChanged(nameof(HasSuggestions));
        HasError = false;
        IsBusy = true;
        StatusMessage = "Checking… this uses the provider set in Settings.";

        _cts = new CancellationTokenSource();
        try
        {
            var selection = _buildSelection(SelectedScope.Scope, _sectionId);
            if (!selection.HasFields)
            {
                StatusMessage = "There's no text to check in this scope.";
                return;
            }

            var result = await _provider.ReviewAsync(selection.Fields, _cts.Token);

            if (!result.Success)
            {
                HasError = true;
                StatusMessage = result.Message ?? "The grammar check failed.";
                return;
            }

            foreach (var s in result.Suggestions)
            {
                if (!selection.BackMap.TryGetValue(s.FieldId, out var field))
                    continue;
                var replaceable = selection.Replaceable.TryGetValue(s.FieldId, out var r) && r;
                Suggestions.Add(new GrammarSuggestionRow(s, field, replaceable, field.Label));
            }

            OnPropertyChanged(nameof(HasSuggestions));
            StatusMessage = Suggestions.Count == 0
                ? "No grammar suggestions — looks good."
                : $"{Suggestions.Count} suggestion{(Suggestions.Count == 1 ? "" : "s")}.";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            RelayCommand.RaiseCanExecuteChanged();
        }
    }

    public void Accept(GrammarSuggestionRow row)
    {
        if (row is null || !row.CanAccept) return;
        TryApply(row);
        Suggestions.Remove(row);
        OnPropertyChanged(nameof(HasSuggestions));
        RelayCommand.RaiseCanExecuteChanged();
    }

    public void Reject(GrammarSuggestionRow row)
    {
        if (row is null) return;
        Suggestions.Remove(row);
        OnPropertyChanged(nameof(HasSuggestions));
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void AcceptAll()
    {
        // Snapshot the acceptable rows, apply each (the applier's offset guard
        // skips any whose span shifted from an earlier accept), then remove all
        // of them from the list regardless — an unapplied one is stale and a
        // re-run will resurface it correctly.
        var toApply = Suggestions.Where(s => s.CanAccept).ToList();
        foreach (var row in toApply)
            TryApply(row);
        foreach (var row in toApply)
            Suggestions.Remove(row);

        OnPropertyChanged(nameof(HasSuggestions));
        StatusMessage = "Applied. Use Ctrl+Z to undo if needed.";
        RelayCommand.RaiseCanExecuteChanged();
    }

    private void TryApply(GrammarSuggestionRow row)
    {
        try
        {
            _applier.Apply(row.Field, row.Suggestion.Start, row.Suggestion.Length, row.Suggestion.Rewrite);
        }
        catch (InvalidOperationException)
        {
            // Offsets shifted (a prior accept in the same field). Skip; the user
            // can re-run to realign remaining suggestions.
        }
    }

    private static IReadOnlyList<GrammarScopeOption> BuildScopeOptions(Guid? sectionId, string? sectionLabel)
    {
        var options = new List<GrammarScopeOption>();
        if (sectionId is not null)
            options.Add(new GrammarScopeOption(GrammarScope.Section,
                string.IsNullOrWhiteSpace(sectionLabel) ? "This section" : $"Section: {sectionLabel}"));
        options.Add(new GrammarScopeOption(GrammarScope.StudyCards, "Study cards"));
        options.Add(new GrammarScopeOption(GrammarScope.WholeQuiz, "Whole quiz"));
        return options;
    }
}

/// <summary>A selectable scope with a label for the dropdown.</summary>
public sealed class GrammarScopeOption
{
    public GrammarScopeOption(GrammarScope scope, string label)
    {
        Scope = scope;
        Label = label;
    }

    public GrammarScope Scope { get; }
    public string Label { get; }
}
