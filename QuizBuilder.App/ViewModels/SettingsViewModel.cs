using System.Collections.ObjectModel;
using System.IO;
using QuizBuilder.App.Services;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.App.ViewModels;

/// <summary>A default point value, one per question type.</summary>
/// <summary>
/// One section's "how many questions to include" row, for exact-count mode. The
/// count is stored back to settings keyed by section id, so it survives sections
/// being reordered and is ignored cleanly if a section is later deleted.
/// </summary>
public sealed class SectionCountRow : ViewModelBase
{
    private readonly Action<Guid, int> _onChanged;
    private int _count;

    public SectionCountRow(Guid sectionId, string title, int poolSize, int count, Action<Guid, int> onChanged)
    {
        SectionId = sectionId;
        Title = title;
        PoolSize = poolSize;
        _count = count;
        _onChanged = onChanged;
    }

    public Guid SectionId { get; }
    public string Title { get; }
    public int PoolSize { get; }

    public string PoolLabel => PoolSize == 1 ? "of 1 question" : $"of {PoolSize} questions";

    public int Count
    {
        get => _count;
        set
        {
            // Clamp to the section's pool: asking for more than it holds just
            // means "all", and a negative is meaningless.
            var clamped = Math.Clamp(value, 0, PoolSize);
            if (SetProperty(ref _count, clamped))
                _onChanged(SectionId, clamped);
        }
    }
}

public sealed class DefaultPointsRow : ViewModelBase
{
    private readonly Action<QuestionKind, double> _onChanged;
    private double _points;

    public DefaultPointsRow(QuestionKind kind, string label, double points,
                            Action<QuestionKind, double> onChanged)
    {
        Kind = kind;
        Label = label;
        _points = points;
        _onChanged = onChanged;
    }

    public QuestionKind Kind { get; }
    public string Label { get; }

    public double Points
    {
        get => _points;
        set
        {
            // Clamp before storing: a negative default would silently produce
            // a quiz where answering correctly loses marks.
            var clamped = Math.Clamp(value, 0, 1000);
            if (SetProperty(ref _points, clamped))
                _onChanged(Kind, clamped);
        }
    }
}

/// <summary>
/// Settings.
///
/// Every property writes straight through to AppSettings and saves. There is
/// no apply/cancel: the app is portable and single-user, so an immediate write
/// is simpler to reason about than a staged edit buffer, and it means a crash
/// never loses a preference.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IQuizDocumentService _document;
    private readonly IAutoSaveService _autoSave;
    private readonly IUndoService _undo;
    private readonly SpellIgnoreListStore _spellDictionary;

    public SettingsViewModel(
        ISettingsService settings,
        IQuizDocumentService document,
        IAutoSaveService autoSave,
        IUndoService undo,
        SpellIgnoreListStore spellDictionary)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _autoSave = autoSave ?? throw new ArgumentNullException(nameof(autoSave));
        _undo = undo ?? throw new ArgumentNullException(nameof(undo));
        _spellDictionary = spellDictionary ?? throw new ArgumentNullException(nameof(spellDictionary));

        // The saved depth is only a number until it is pushed into the service
        // that enforces it.
        _undo.SetDepth(Undo.Depth);

        DefaultPoints = BuildDefaultPointsRows();
        RefreshSections();
        RefreshSpellWords();

        AddSpellWordCommand = new RelayCommand(AddSpellWord, () => CanAddSpellWord);

        ResetDefaultsCommand = new RelayCommand(ResetDefaults);
        ClearTokenCommand = new RelayCommand(
            () => { _settings.SetGitHubToken(null); Save(); OnPropertyChanged(nameof(HasStoredToken)); },
            () => HasStoredToken);
    }

    private QuizSettings Quiz => _settings.Current.Quiz;

    private AutoSaveSettings AutoSave => _settings.Current.AutoSave;
    private UndoSettings Undo => _settings.Current.Undo;

    public string SettingsFilePath => _settings.SettingsFilePath;

    public bool SettingsFileExists => File.Exists(_settings.SettingsFilePath);

    // --- Grading scope ----------------------------------------------------

    public bool GradeAllSections
    {
        get => Quiz.GradingScope == GradingScope.AllSections;
        set
        {
            if (!value) return;   // radio buttons: only react to the checked one
            Quiz.GradingScope = GradingScope.AllSections;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectSectionsAtQuizTime));
        }
    }

    public bool SelectSectionsAtQuizTime
    {
        get => Quiz.GradingScope == GradingScope.SelectAtQuizTime;
        set
        {
            if (!value) return;
            Quiz.GradingScope = GradingScope.SelectAtQuizTime;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(GradeAllSections));
        }
    }

    // --- Question selection -----------------------------------------------

    public bool UseAllQuestions
    {
        get => Quiz.SelectionMode == QuestionSelectionMode.AllQuestions;
        set
        {
            if (!value) return;
            Quiz.SelectionMode = QuestionSelectionMode.AllQuestions;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseExactCount));
            OnPropertyChanged(nameof(UseTotalCount));
            OnPropertyChanged(nameof(ShowPerSectionCounts));
            OnPropertyChanged(nameof(ShowTotalCount));
        }
    }

    public bool UseExactCount
    {
        get => Quiz.SelectionMode == QuestionSelectionMode.ExactCountPerSection;
        set
        {
            if (!value) return;
            Quiz.SelectionMode = QuestionSelectionMode.ExactCountPerSection;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseAllQuestions));
            OnPropertyChanged(nameof(UseTotalCount));
            OnPropertyChanged(nameof(ShowPerSectionCounts));
            OnPropertyChanged(nameof(ShowTotalCount));
        }
    }

    public bool UseTotalCount
    {
        get => Quiz.SelectionMode == QuestionSelectionMode.TotalCount;
        set
        {
            if (!value) return;
            Quiz.SelectionMode = QuestionSelectionMode.TotalCount;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseAllQuestions));
            OnPropertyChanged(nameof(UseExactCount));
            OnPropertyChanged(nameof(ShowPerSectionCounts));
            OnPropertyChanged(nameof(ShowTotalCount));
        }
    }

    /// <summary>
    /// The quiz-wide total for TotalCount mode. Clamped between 0 and the number
    /// of questions the quiz actually has, so it can never ask for more than
    /// exists. LostFocus binding, like the other numeric fields, so the clamp
    /// does not fight the user mid-type.
    /// </summary>
    public int TotalQuestionCount
    {
        get => Quiz.TotalQuestionCount;
        set
        {
            var clamped = Math.Clamp(value, 0, TotalAvailableQuestions);
            if (Quiz.TotalQuestionCount == clamped) return;

            Quiz.TotalQuestionCount = clamped;
            Save();

            OnPropertyChanged();
            OnPropertyChanged(nameof(TotalCountSummary));
        }
    }

    /// <summary>Every question in the quiz, the ceiling for the total-count field.</summary>
    public int TotalAvailableQuestions => _document.Current.Sections.Sum(s => s.Questions.Count);

    public string TotalCountSummary
    {
        get
        {
            var available = TotalAvailableQuestions;
            if (available == 0) return "Add questions first.";

            var n = Math.Clamp(Quiz.TotalQuestionCount, 0, available);

            if (n == 0) return "No questions will appear.";
            if (n >= available) return $"All {available} questions will appear.";

            return $"{n} of {available} questions, spread across sections by size.";
        }
    }

    public bool ShowTotalCount => UseTotalCount && _document.Current.Sections.Count > 0;

    /// <summary>
    /// The per-section count editor only makes sense in exact-count mode, and
    /// only when sections exist. Showing empty controls invites the user to
    /// configure something with no effect.
    /// </summary>
    public bool ShowPerSectionCounts => UseExactCount && _document.Current.Sections.Count > 0;

    public bool HasNoSections => _document.Current.Sections.Count == 0;

    /// <summary>Per-section count rows for exact-count mode, in display order.</summary>
    public ObservableCollection<SectionCountRow> SectionCounts { get; } = new();

    /// <summary>
    /// Rebuilds the per-section rows and re-evaluates the selection-related
    /// visibility flags. Called when the Settings tab becomes visible, so that
    /// sections added, removed, or reordered on the Quiz Builder tab are
    /// reflected without the two tabs having to stay wired together live.
    /// </summary>
    public void RefreshSections()
    {
        SectionCounts.Clear();

        foreach (var section in _document.Current.SectionsInDisplayOrder())
        {
            var pool = section.Questions.Count;

            // Default an unconfigured section to its full pool, matching the
            // compiler, which treats "no configured count" as "take all".
            var count = Quiz.QuestionCountPerSection.TryGetValue(section.Id.ToString(), out var stored)
                ? Math.Clamp(stored, 0, pool)
                : pool;

            SectionCounts.Add(new SectionCountRow(section.Id, section.Title, pool, count, OnSectionCountChanged));
        }

        OnPropertyChanged(nameof(ShowPerSectionCounts));
        OnPropertyChanged(nameof(ShowTotalCount));
        OnPropertyChanged(nameof(HasNoSections));
        OnPropertyChanged(nameof(TotalAvailableQuestions));
        OnPropertyChanged(nameof(TotalCountSummary));

        // A stored total above the current quiz size would be stale; clamp it.
        var available = TotalAvailableQuestions;
        if (Quiz.TotalQuestionCount > available)
        {
            Quiz.TotalQuestionCount = available;
            Save();
            OnPropertyChanged(nameof(TotalQuestionCount));
        }
    }

    private void OnSectionCountChanged(Guid sectionId, int count)
    {
        Quiz.QuestionCountPerSection[sectionId.ToString()] = count;
        Save();
    }

    // --- Pass mark --------------------------------------------------------

    /// <summary>
    /// Percentage of the paper's points needed to pass. Bound with LostFocus
    /// rather than PropertyChanged: the setter clamps, so typing "100" would
    /// have the intermediate "1" clamped and echoed back, making the field
    /// impossible to type into. Same trap as the section name.
    /// </summary>
    public int PassPercentage
    {
        get => Quiz.PassPercentage;
        set
        {
            var clamped = Math.Clamp(value, QuizSettings.MinPassPercentage, QuizSettings.MaxPassPercentage);
            if (Quiz.PassPercentage == clamped) return;

            Quiz.PassPercentage = clamped;
            Save();

            OnPropertyChanged();
            OnPropertyChanged(nameof(PassMarkSummary));
            OnPropertyChanged(nameof(ShowPassMarkWarning));
        }
    }

    // Two properties rather than one enum binding, because RadioButton.IsChecked
    // binds one-way-per-button. Setting either updates the model and notifies
    // both, so the pair can never disagree.
    public bool PassByQuestionCount
    {
        get => Quiz.PassMarkBasis == PassMarkBasis.QuestionCount;
        set
        {
            if (!value || PassByQuestionCount) return;

            Quiz.PassMarkBasis = PassMarkBasis.QuestionCount;
            Save();
            NotifyPassMarkChanged();
        }
    }

    public bool PassByTotalPoints
    {
        get => Quiz.PassMarkBasis == PassMarkBasis.TotalPoints;
        set
        {
            if (!value || PassByTotalPoints) return;

            Quiz.PassMarkBasis = PassMarkBasis.TotalPoints;
            Save();
            NotifyPassMarkChanged();
        }
    }

    private void NotifyPassMarkChanged()
    {
        OnPropertyChanged(nameof(PassByQuestionCount));
        OnPropertyChanged(nameof(PassByTotalPoints));
        OnPropertyChanged(nameof(PassMarkSummary));
        OnPropertyChanged(nameof(PassMarkBasisHint));
    }

    // Flash card source: three radio buttons, one bool each, same shape as the
    // pass-mark pair above. Setting one updates the model and notifies all three,
    // so exactly one reads true.
    public bool FlashFromQuiz
    {
        get => Quiz.FlashCardSource == FlashCardSource.Quiz;
        set
        {
            if (!value || FlashFromQuiz) return;

            Quiz.FlashCardSource = FlashCardSource.Quiz;
            Save();
            NotifyFlashSourceChanged();
        }
    }

    public bool FlashFromStudyCards
    {
        get => Quiz.FlashCardSource == FlashCardSource.StudyCards;
        set
        {
            if (!value || FlashFromStudyCards) return;

            Quiz.FlashCardSource = FlashCardSource.StudyCards;
            Save();
            NotifyFlashSourceChanged();
        }
    }

    public bool FlashFromBoth
    {
        get => Quiz.FlashCardSource == FlashCardSource.Both;
        set
        {
            if (!value || FlashFromBoth) return;

            Quiz.FlashCardSource = FlashCardSource.Both;
            Save();
            NotifyFlashSourceChanged();
        }
    }

    private void NotifyFlashSourceChanged()
    {
        OnPropertyChanged(nameof(FlashFromQuiz));
        OnPropertyChanged(nameof(FlashFromStudyCards));
        OnPropertyChanged(nameof(FlashFromBoth));
    }

    /// <summary>
    /// Spells out the difference, because it only shows up on a weighted paper
    /// and is easy to pick wrongly without noticing.
    /// </summary>
    public string PassMarkBasisHint => PassByQuestionCount
        ? "Every question counts equally, whatever it is worth. A question counts as correct "
          + "when it scores at least half its points."
        : "Questions worth more points count for more. A 10-point essay carries ten times the "
          + "weight of a 1-point true/false.";

    /// <summary>
    /// Restates the setting against the actual paper, so the number means
    /// something concrete rather than a bare percentage.
    /// </summary>
    public string PassMarkSummary
    {
        get
        {
            if (PassByQuestionCount)
            {
                var gradeable = _document.Current.Sections
                    .SelectMany(s => s.Questions)
                    .Count(q => q.Points > 0);

                if (gradeable == 0) return "Add some questions to see what this means.";

                var needed = (int)Math.Ceiling(gradeable * Quiz.PassPercentage / 100d);
                var label = needed == 1 ? "question" : "questions";

                return $"{needed} of {gradeable} {label} must be correct to pass.";
            }

            var total = _document.Current.TotalPoints;
            if (total <= 0) return "Add some questions to see what this means.";

            var points = Math.Ceiling(total * Quiz.PassPercentage) / 100d;
            return $"{points:0.##} of {total:0.##} points needed to pass.";
        }
    }

    public bool ShowPassMarkWarning => Quiz.PassPercentage == 0;

    // --- Randomisation ----------------------------------------------------

    public bool RandomizeQuestionOrder
    {
        get => Quiz.RandomizeQuestionOrder;
        set { Quiz.RandomizeQuestionOrder = value; Save(); OnPropertyChanged(); }
    }

    public bool RandomizeAnswerOrder
    {
        get => Quiz.RandomizeAnswerOrder;
        set { Quiz.RandomizeAnswerOrder = value; Save(); OnPropertyChanged(); }
    }

    // --- Timing -----------------------------------------------------------

    public bool HasTimeLimit
    {
        get => Quiz.TimeLimitMinutes.HasValue;
        set
        {
            // Default to 30 when enabling, rather than 0: a zero-minute limit
            // would end the quiz the moment it started.
            Quiz.TimeLimitMinutes = value ? Quiz.TimeLimitMinutes ?? 30 : null;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimeLimitMinutes));
        }
    }

    public int TimeLimitMinutes
    {
        get => Quiz.TimeLimitMinutes ?? 30;
        set
        {
            if (!HasTimeLimit) return;
            Quiz.TimeLimitMinutes = Math.Clamp(value, 1, 600);
            Save();
            OnPropertyChanged();
        }
    }

    // --- Default point values ---------------------------------------------

    public IReadOnlyList<DefaultPointsRow> DefaultPoints { get; }

    public RelayCommand ResetDefaultsCommand { get; }

    private IReadOnlyList<DefaultPointsRow> BuildDefaultPointsRows()
    {
        var labels = new (QuestionKind Kind, string Label)[]
        {
            (QuestionKind.MultipleChoiceSingle, "Multiple choice (one answer)"),
            (QuestionKind.MultipleChoiceMultiple, "Multiple choice (several answers)"),
            (QuestionKind.TrueFalse, "True / False"),
            (QuestionKind.ShortAnswer, "Short answer"),
            (QuestionKind.FillInTheBlank, "Fill in the blank"),
            (QuestionKind.Matching, "Matching"),
            (QuestionKind.Sequence, "Sequence"),
            (QuestionKind.Essay, "Essay"),
        };

        return labels
            .Select(l => new DefaultPointsRow(l.Kind, l.Label, Quiz.PointsFor(l.Kind), OnPointsChanged))
            .ToList();
    }

    private void OnPointsChanged(QuestionKind kind, double points)
    {
        Quiz.DefaultPoints[kind.ToString()] = points;
        Save();
    }

    private void ResetDefaults()
    {
        var fresh = new QuizSettings();

        foreach (var row in DefaultPoints)
            row.Points = fresh.PointsFor(row.Kind);
    }

    // --- Autosave ---------------------------------------------------------

    public bool AutoSaveEnabled
    {
        get => AutoSave.Enabled;
        set
        {
            AutoSave.Enabled = value;
            Save();

            // Apply immediately rather than at next launch: a setting that
            // needs a restart to take effect is a setting people think is
            // broken.
            _autoSave.Reconfigure();

            OnPropertyChanged();
            OnPropertyChanged(nameof(AutoSaveStatus));
        }
    }

    public int AutoSaveIntervalMinutes
    {
        get => AutoSave.IntervalMinutes;
        set
        {
            AutoSave.IntervalMinutes = Math.Clamp(
                value,
                AutoSaveSettings.MinIntervalMinutes,
                AutoSaveSettings.MaxIntervalMinutes);

            Save();
            _autoSave.Reconfigure();

            OnPropertyChanged();
            OnPropertyChanged(nameof(AutoSaveStatus));
        }
    }

    public int AutoSaveMinInterval => AutoSaveSettings.MinIntervalMinutes;
    public int AutoSaveMaxInterval => AutoSaveSettings.MaxIntervalMinutes;

    /// <summary>
    /// How many structural changes can be stepped back through. Each step is a
    /// full copy of the document, so this is a memory/safety trade rather than
    /// a free dial.
    /// </summary>
    public int UndoDepth
    {
        get => Undo.Depth;
        set
        {
            Undo.Depth = Math.Clamp(value, UndoSettings.MinDepth, UndoSettings.MaxDepth);

            Save();

            // Apply now rather than at next launch. Lowering the depth also
            // has to trim history that is already held, or the number shown
            // here would not match what the app would actually give back.
            _undo.SetDepth(Undo.Depth);

            OnPropertyChanged();
            OnPropertyChanged(nameof(UndoStatus));
        }
    }

    public int UndoMinDepth => UndoSettings.MinDepth;
    public int UndoMaxDepth => UndoSettings.MaxDepth;

    /// <summary>
    /// Spells out what the number means, including that zero is off and that
    /// typing inside an editor is not covered by it.
    /// </summary>
    public string UndoStatus =>
        UndoDepth == 0
            ? "Undo is off. Structural changes cannot be stepped back."
            : $"Steps back through the last {UndoDepth} structural change{(UndoDepth == 1 ? "" : "s")} "
              + "(adding, deleting, reordering, renaming). Typing inside a question is undone by the text box itself.";

    /// <summary>
    /// Autosave can only write to a file that already exists. Rather than let
    /// the user switch it on and quietly discover it never fired, the state is
    /// stated plainly.
    /// </summary>
    public bool SessionHasFile => !string.IsNullOrEmpty(_document.CurrentFilePath);

    public bool ShowAutoSaveCaveat => AutoSaveEnabled && !SessionHasFile;

    public string AutoSaveStatus
    {
        get
        {
            if (!AutoSaveEnabled) return "Off.";

            if (!SessionHasFile)
                return "Waiting for a saved session. Autosave starts once you save this quiz to a .qbx file.";

            return $"Saving to {Path.GetFileName(_document.CurrentFilePath)} every "
                   + $"{AutoSaveIntervalMinutes} minute{(AutoSaveIntervalMinutes == 1 ? "" : "s")}.";
        }
    }

    // --- Token protection --------------------------------------------------

    private GitHubSettings GitHub => _settings.Current.GitHub;

    public bool HasStoredToken => !string.IsNullOrEmpty(GitHub.EncryptedToken);

    public RelayCommand ClearTokenCommand { get; }

    public bool TokenMachineBound
    {
        get => GitHub.TokenProtection == TokenProtectionMode.MachineBound;
        set { if (value) SetTokenMode(TokenProtectionMode.MachineBound); }
    }

    public bool TokenPassphrase
    {
        get => GitHub.TokenProtection == TokenProtectionMode.Passphrase;
        set { if (value) SetTokenMode(TokenProtectionMode.Passphrase); }
    }

    public bool TokenNone
    {
        get => GitHub.TokenProtection == TokenProtectionMode.None;
        set { if (value) SetTokenMode(TokenProtectionMode.None); }
    }

    /// <summary>
    /// Shown next to the mode picker. Switching modes CLEARS any stored token,
    /// because ciphertext is not transcoded between modes. Saying so before the
    /// fact is the difference between a documented trade-off and silent data
    /// loss.
    /// </summary>
    public string TokenModeWarning =>
        HasStoredToken
            ? "Changing this clears your saved GitHub token. You will need to enter it again."
            : string.Empty;

    public bool ShowTokenModeWarning => HasStoredToken;

    private void SetTokenMode(TokenProtectionMode mode)
    {
        _settings.SetTokenProtectionMode(mode);
        Save();

        OnPropertyChanged(nameof(TokenMachineBound));
        OnPropertyChanged(nameof(TokenPassphrase));
        OnPropertyChanged(nameof(TokenNone));
        OnPropertyChanged(nameof(HasStoredToken));
        OnPropertyChanged(nameof(TokenModeWarning));
        OnPropertyChanged(nameof(ShowTokenModeWarning));
        RelayCommand.RaiseCanExecuteChanged();
    }

    // ----- AI grammar review (opt-in; Phase 1 = settings + key only) -------- //

    private AiReviewSettings Ai => _settings.Current.AiReview;

    /// <summary>The provider options in UI order: Off, Local endpoint (privacy-
    /// first), then Claude. Bound to the dropdown.</summary>
    public IReadOnlyList<AiProviderOption> AiProviderOptions { get; } = new[]
    {
        new AiProviderOption(AiProvider.Off, "Off — no AI review (default)"),
        new AiProviderOption(AiProvider.LocalEndpoint, "Local endpoint (stays on your machine/network)"),
        new AiProviderOption(AiProvider.Claude, "Claude (sends text to Anthropic)"),
    };

    public AiProviderOption SelectedAiProvider
    {
        get => AiProviderOptions.First(o => o.Provider == Ai.Provider);
        set
        {
            if (value is null || value.Provider == Ai.Provider) return;
            Ai.Provider = value.Provider;
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLocalEndpointSelected));
            OnPropertyChanged(nameof(IsClaudeSelected));
            OnPropertyChanged(nameof(ShowsCloudNotice));
            OnPropertyChanged(nameof(NeedsApiKey));
        }
    }

    public bool IsLocalEndpointSelected => Ai.Provider == AiProvider.LocalEndpoint;
    public bool IsClaudeSelected => Ai.Provider == AiProvider.Claude;

    /// <summary>Shown when the selected provider sends content off-device, so the
    /// privacy implication is never hidden.</summary>
    public bool ShowsCloudNotice => Ai.Provider == AiProvider.Claude;

    /// <summary>Claude needs a key; a local endpoint usually doesn't.</summary>
    public bool NeedsApiKey => Ai.Provider == AiProvider.Claude;

    public string? AiLocalEndpointUrl
    {
        get => Ai.LocalEndpointUrl;
        set
        {
            if (Ai.LocalEndpointUrl == value) return;
            Ai.LocalEndpointUrl = value;
            Save();
            OnPropertyChanged();
        }
    }

    public string? AiModel
    {
        get => Ai.Model;
        set
        {
            if (Ai.Model == value) return;
            Ai.Model = value;
            Save();
            OnPropertyChanged();
        }
    }

    public bool HasAiKey => _settings.HasAiReviewKey;

    /// <summary>
    /// Sets (and encrypts) the AI key from the password box. Not a bound
    /// property — the plaintext key is passed in at the call site and never held
    /// in a VM field, mirroring how the GitHub token is handled.
    /// </summary>
    public void SetAiKey(string? plainKey)
    {
        _settings.SetAiReviewKey(plainKey);
        Save();
        OnPropertyChanged(nameof(HasAiKey));
    }

    public RelayCommand ClearAiKeyCommand => _clearAiKeyCommand ??= new RelayCommand(
        () => { _settings.SetAiReviewKey(null); Save(); OnPropertyChanged(nameof(HasAiKey)); },
        () => HasAiKey);
    private RelayCommand? _clearAiKeyCommand;

    private void Save()
    {
        _settings.Save();
        OnPropertyChanged(nameof(SettingsFileExists));
    }

    // ----- Spelling dictionary (custom words the checker treats as correct) -- //

    /// <summary>The words the user has added to their custom spelling dictionary,
    /// shown in Settings so they can be reviewed and removed. Each row wraps one
    /// word plus a remove command.</summary>
    public ObservableCollection<SpellWordRow> SpellWords { get; } = new();

    public bool HasSpellWords => SpellWords.Count > 0;

    public bool HasNoSpellWords => SpellWords.Count == 0;

    private string _newSpellWord = string.Empty;
    public string NewSpellWord
    {
        get => _newSpellWord;
        set
        {
            if (SetProperty(ref _newSpellWord, value))
            {
                OnPropertyChanged(nameof(CanAddSpellWord));
                RelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool CanAddSpellWord => !string.IsNullOrWhiteSpace(NewSpellWord);

    public RelayCommand AddSpellWordCommand { get; }

    private void AddSpellWord()
    {
        if (_spellDictionary.Add(NewSpellWord))
        {
            NewSpellWord = string.Empty;
            RefreshSpellWords();
        }
        else
        {
            // Already present (or blank): just clear the box so it's clear the
            // word is known, without a duplicate row.
            NewSpellWord = string.Empty;
        }
    }

    private void RemoveSpellWord(string word)
    {
        if (_spellDictionary.Remove(word))
            RefreshSpellWords();
    }

    public void RefreshSpellWords()
    {
        SpellWords.Clear();
        foreach (var word in _spellDictionary.GetWords()
                     .OrderBy(w => w, StringComparer.CurrentCultureIgnoreCase))
        {
            SpellWords.Add(new SpellWordRow(word, new RelayCommand(() => RemoveSpellWord(word))));
        }
        OnPropertyChanged(nameof(HasSpellWords));
        OnPropertyChanged(nameof(HasNoSpellWords));
    }
}

/// <summary>One word in the custom spelling dictionary, with a command to remove it.</summary>
public sealed class SpellWordRow
{
    public SpellWordRow(string word, RelayCommand removeCommand)
    {
        Word = word;
        RemoveCommand = removeCommand;
    }

    public string Word { get; }
    public RelayCommand RemoveCommand { get; }
}

/// <summary>A selectable AI provider with a human-readable label for the dropdown.</summary>
public sealed class AiProviderOption
{
    public AiProviderOption(AiProvider provider, string label)
    {
        Provider = provider;
        Label = label;
    }

    public AiProvider Provider { get; }
    public string Label { get; }
}
