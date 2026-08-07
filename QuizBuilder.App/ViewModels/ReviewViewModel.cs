using System.Collections.Generic;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;

namespace QuizBuilder.App.ViewModels;

/// <summary>
/// The Review tab: spaced-repetition study over the quiz's flash cards. A thin
/// WPF wrapper over <see cref="ReviewSession"/> — the scheduling and due-queue
/// logic live in Core (tested on Linux); this layer holds the "which card am I
/// looking at, is it flipped" view state and forwards grades.
///
/// Flow: the session hands us the cards due today. We show one front, the user
/// reveals the back, then grades it (Again/Hard/Good/Easy). Grading advances the
/// card's schedule via Core and moves to the next due card. When the queue is
/// empty we show a "done for today" state.
/// </summary>
public sealed class ReviewViewModel : ViewModelBase
{
    private readonly IQuizDocumentService _document;
    private readonly IReviewProgressStore _store;
    private readonly IQuizPackageService _images;

    private List<StudyCard> _due = new();
    private int _index;
    private bool _showingBack;
    private bool _isVisible;
    private bool _isStale = true;

    public ReviewViewModel(
        IQuizDocumentService document,
        IReviewProgressStore store,
        IQuizPackageService images)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _images = images ?? throw new ArgumentNullException(nameof(images));

        // Cards added/edited or progress reset elsewhere both change what's due.
        _document.DocumentChanged += (_, _) => MarkStale();
        _store.ProgressChanged += (_, _) => MarkStale();

        RevealCommand = new RelayCommand(Reveal, () => HasCard && !_showingBack);
        AgainCommand = new RelayCommand(() => Grade(ReviewGrade.Again), () => CanGrade);
        HardCommand = new RelayCommand(() => Grade(ReviewGrade.Hard), () => CanGrade);
        GoodCommand = new RelayCommand(() => Grade(ReviewGrade.Good), () => CanGrade);
        EasyCommand = new RelayCommand(() => Grade(ReviewGrade.Easy), () => CanGrade);
        RestartCommand = new RelayCommand(Rebuild, () => !_isVisible || _isStale || IsDone);
    }

    public RelayCommand RevealCommand { get; }
    public RelayCommand AgainCommand { get; }
    public RelayCommand HardCommand { get; }
    public RelayCommand GoodCommand { get; }
    public RelayCommand EasyCommand { get; }
    public RelayCommand RestartCommand { get; }

    /// <summary>Called by the shell when this tab becomes visible.</summary>
    public void OnActivated()
    {
        _isVisible = true;
        if (_isStale) Rebuild();
    }

    public void OnDeactivated() => _isVisible = false;

    private void MarkStale()
    {
        _isStale = true;
        if (_isVisible) Rebuild();
    }

    private void Rebuild()
    {
        _hasAnyStudyCards = _document.Current.StudyCards.Count > 0;
        var session = new ReviewSession(_store, _document.Current);
        _due = new List<StudyCard>(session.DueCards());
        _index = 0;
        _showingBack = false;
        _isStale = false;
        RaiseAll();
    }

    private bool _hasAnyStudyCards;

    private ReviewSession CurrentSession() => new(_store, _document.Current);

    // ----- Current card ---------------------------------------------------- //

    public bool HasCard => _index < _due.Count;
    public StudyCard? Current => HasCard ? _due[_index] : null;
    public bool ShowingBack => _showingBack;
    private bool CanGrade => HasCard && _showingBack;

    /// <summary>True when there were cards due and we've graded the last one.</summary>
    public bool IsDone => !HasCard && _reviewedAny;

    /// <summary>True when nothing was due to begin with (fresh, all caught up).</summary>
    public bool NoStudyCards => !HasCard && !_reviewedAny && !_hasAnyStudyCards;
    public bool AllCaughtUp => !HasCard && !_reviewedAny && _hasAnyStudyCards;

    private bool _reviewedAny;

    /// <summary>The text of the face currently showing.</summary>
    public string FaceText => Current is null
        ? string.Empty
        : (_showingBack ? Current.Back : Current.Front);

    /// <summary>The image for the current face, or null.</summary>
    public byte[]? CurrentImageBytes
    {
        get
        {
            if (Current is null) return null;
            var path = _showingBack ? Current.BackImageRelativePath : Current.FrontImageRelativePath;
            return string.IsNullOrEmpty(path) ? null : _images.GetImage(path);
        }
    }

    public bool HasCurrentImage => CurrentImageBytes is not null;

    /// <summary>"3 left" — how many due cards remain, including the current one.</summary>
    public string RemainingLabel
    {
        get
        {
            var remaining = _due.Count - _index;
            return remaining == 1 ? "1 card left" : $"{remaining} cards left";
        }
    }

    public string FlipHint => _showingBack
        ? "How well did you know it?"
        : "Click the card to reveal the answer";

    // ----- Actions --------------------------------------------------------- //

    private void Reveal()
    {
        if (!HasCard || _showingBack) return;
        _showingBack = true;
        RaiseAll();
    }

    private void Grade(ReviewGrade grade)
    {
        if (!CanGrade) return;

        var card = _due[_index];
        CurrentSession().Grade(card.Id, grade);   // advances schedule + persists
        _reviewedAny = true;

        _index++;
        _showingBack = false;
        RaiseAll();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(HasCard));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(ShowingBack));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(NoStudyCards));
        OnPropertyChanged(nameof(AllCaughtUp));
        OnPropertyChanged(nameof(FaceText));
        OnPropertyChanged(nameof(CurrentImageBytes));
        OnPropertyChanged(nameof(HasCurrentImage));
        OnPropertyChanged(nameof(RemainingLabel));
        OnPropertyChanged(nameof(FlipHint));
        RelayCommand.RaiseCanExecuteChanged();
    }
}
