using System.Collections.ObjectModel;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.App.ViewModels;

/// <summary>One bank entry, as a row: a preview of the question and its category.</summary>
public sealed class BankEntryRowViewModel : ViewModelBase
{
    private readonly Action<Guid, string?> _setCategory;
    private string? _category;

    public BankEntryRowViewModel(BankEntry entry, Action<Guid, string?> setCategory, Action<Guid> remove, Action<Guid> addToQuiz)
    {
        Entry = entry;
        _setCategory = setCategory;
        _category = entry.Category;

        TypeLabel = entry.Question.KindDisplayName;
        Prompt = string.IsNullOrWhiteSpace(entry.Question.Prompt)
            ? "(no prompt)"
            : entry.Question.Prompt;

        RemoveCommand = new RelayCommand(() => remove(entry.Id));
        AddToQuizCommand = new RelayCommand(() => addToQuiz(entry.Id));
    }

    public BankEntry Entry { get; }
    public string Prompt { get; }
    public string TypeLabel { get; }

    public RelayCommand RemoveCommand { get; }
    public RelayCommand AddToQuizCommand { get; }

    /// <summary>Editable category; committed to the store on change.</summary>
    public string? Category
    {
        get => _category;
        set
        {
            if (_category == value) return;

            _category = value;
            OnPropertyChanged();
            _setCategory(Entry.Id, value);
        }
    }
}

/// <summary>
/// The Question Bank tab: a reusable pool of questions. Authors save questions
/// here from the builder, organise them with a category, filter by it, and add
/// a copy into the current quiz's chosen section.
/// </summary>
public sealed class QuestionBankViewModel : ViewModelBase
{
    private readonly IQuestionBankService _bank;
    private readonly IQuizDocumentService _document;
    private readonly IUndoService _undo;

    private bool _isVisible;
    private bool _isStale = true;
    private string _categoryFilter = AllCategories;
    private SectionTargetViewModel? _targetSection;

    private const string AllCategories = "All categories";

    public QuestionBankViewModel(
        IQuestionBankService bank,
        IQuizDocumentService document,
        IUndoService undo)
    {
        _bank = bank ?? throw new ArgumentNullException(nameof(bank));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _undo = undo ?? throw new ArgumentNullException(nameof(undo));

        Entries = new ObservableCollection<BankEntryRowViewModel>();
        CategoryFilters = new ObservableCollection<string> { AllCategories };
        TargetSections = new ObservableCollection<SectionTargetViewModel>();

        _bank.BankChanged += (_, _) => MarkStale();
        _document.DocumentChanged += (_, _) => MarkStale();
    }

    public ObservableCollection<BankEntryRowViewModel> Entries { get; }
    public ObservableCollection<string> CategoryFilters { get; }
    public ObservableCollection<SectionTargetViewModel> TargetSections { get; }

    public bool IsEmpty => Entries.Count == 0;

    /// <summary>Whether there is a section to add a question into.</summary>
    public bool CanAddToQuiz => TargetSections.Count > 0;

    public string EmptyMessage =>
        _bank.All().Count == 0
            ? "No saved questions yet. In the Quiz Builder, use \"Save to bank\" on a question to add it here."
            : "No questions match this category.";

    public string CategoryFilter
    {
        get => _categoryFilter;
        set
        {
            if (_categoryFilter == value) return;

            _categoryFilter = value;
            OnPropertyChanged();
            RebuildEntries();
        }
    }

    /// <summary>The section a pulled question is added to.</summary>
    public SectionTargetViewModel? TargetSection
    {
        get => _targetSection;
        set
        {
            if (ReferenceEquals(_targetSection, value)) return;

            _targetSection = value;
            OnPropertyChanged();
        }
    }

    public void OnActivated()
    {
        _isVisible = true;
        if (_isStale) Refresh();
    }

    public void OnDeactivated() => _isVisible = false;

    private void MarkStale()
    {
        _isStale = true;
        if (_isVisible) Refresh();
    }

    public void Refresh()
    {
        _isStale = false;

        RebuildCategoryFilters();
        RebuildTargetSections();
        RebuildEntries();
    }

    private void RebuildCategoryFilters()
    {
        var selected = _categoryFilter;

        CategoryFilters.Clear();
        CategoryFilters.Add(AllCategories);
        foreach (var category in _bank.Categories())
            CategoryFilters.Add(category);

        // Keep the current selection if it still exists, else fall back to All.
        _categoryFilter = CategoryFilters.Contains(selected) ? selected : AllCategories;
        OnPropertyChanged(nameof(CategoryFilter));
    }

    private void RebuildTargetSections()
    {
        var previousId = _targetSection?.SectionId;

        TargetSections.Clear();
        foreach (var section in _document.Current.SectionsInDisplayOrder())
            TargetSections.Add(new SectionTargetViewModel(section.Id, section.Title));

        _targetSection = TargetSections.FirstOrDefault(s => s.SectionId == previousId)
                         ?? TargetSections.FirstOrDefault();

        OnPropertyChanged(nameof(TargetSection));
        OnPropertyChanged(nameof(CanAddToQuiz));
    }

    private void RebuildEntries()
    {
        Entries.Clear();

        var entries = _bank.All().AsEnumerable();

        if (_categoryFilter != AllCategories)
            entries = entries.Where(e => string.Equals(e.Category, _categoryFilter, StringComparison.OrdinalIgnoreCase));

        foreach (var entry in entries)
            Entries.Add(new BankEntryRowViewModel(entry, _bank.SetCategory, RemoveEntry, AddEntryToQuiz));

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    private void RemoveEntry(Guid entryId) => _bank.Remove(entryId);

    private void AddEntryToQuiz(Guid entryId)
    {
        if (_targetSection is null) return;

        var entry = _bank.All().FirstOrDefault(e => e.Id == entryId);
        if (entry is null) return;

        // Clone so the quiz gets its own copy: editing it in the quiz must not
        // reach the bank, and adding the same bank question to two quizzes must
        // give each an independent question. Clone() also mints a fresh id.
        _undo.CaptureBeforeChange("Add question from bank");
        _document.AddQuestion(_targetSection.SectionId, entry.Question.Clone());
    }
}

/// <summary>A section the author can add a pulled question into.</summary>
public sealed class SectionTargetViewModel
{
    public SectionTargetViewModel(Guid sectionId, string title)
    {
        SectionId = sectionId;
        Title = string.IsNullOrWhiteSpace(title) ? "(untitled section)" : title;
    }

    public Guid SectionId { get; }
    public string Title { get; }
}
