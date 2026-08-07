using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using QuizBuilder.Player.Services;

namespace QuizBuilder.Player.ViewModels;

/// <summary>
/// Spaced-repetition review on mobile. A thin wrapper over Core's
/// <see cref="ReviewSession"/> and <see cref="Sm2Scheduler"/> — the same
/// scheduling the desktop uses, shared directly, so a card graded on the phone
/// advances on exactly the rule the desktop applies. This VM holds only the
/// "which due card, is it revealed" view state and forwards grades.
///
/// Flow: show a due card's front, reveal the back, grade it (Again/Hard/Good/
/// Easy). Grading advances the schedule via Core and moves on. When the due
/// queue empties we show a "done" state.
/// </summary>
public partial class ReviewViewModel : ObservableObject
{
    private readonly QuizSessionService _session;
    private readonly IReviewProgressStore _store;

    private List<StudyCard> _due = new();
    private int _index;

    public ReviewViewModel(QuizSessionService session, IReviewProgressStore store)
    {
        _session = session;
        _store = store;
        Rebuild();
    }

    private QuizDocument? Document => _session.Loaded?.Document;

    private void Rebuild()
    {
        var doc = Document;
        if (doc is null)
        {
            _due = new List<StudyCard>();
            _hasAnyStudyCards = false;
        }
        else
        {
            _hasAnyStudyCards = doc.StudyCards.Count > 0;
            var session = new ReviewSession(_store, doc);
            _due = new List<StudyCard>(session.DueCards());
        }

        _index = 0;
        _reviewedAny = false;
        ShowingBack = false;
        RaiseAll();
    }

    private bool _hasAnyStudyCards;

    // ----- State ------------------------------------------------------------

    [ObservableProperty]
    private bool _showingBack;

    private bool _reviewedAny;

    public bool HasCard => _index < _due.Count;
    public StudyCard? Current => HasCard ? _due[_index] : null;

    /// <summary>Cards were due and we've graded the last one.</summary>
    public bool IsDone => !HasCard && _reviewedAny;

    /// <summary>
    /// The quiz has no study cards at all — the person needs to author some (in
    /// the Study Cards tab on the desktop), not just wait. Distinct from
    /// <see cref="AllCaughtUp"/>, which means cards exist but none are due yet.
    /// </summary>
    public bool NoStudyCards => !HasCard && !_reviewedAny && !_hasAnyStudyCards;

    /// <summary>
    /// The quiz has study cards, but none are due right now — everything's been
    /// reviewed recently. Coming back later will surface the next batch.
    /// </summary>
    public bool AllCaughtUp => !HasCard && !_reviewedAny && _hasAnyStudyCards;

    /// <summary>The text on the face currently showing.</summary>
    public string FaceText => Current is null
        ? string.Empty
        : (ShowingBack ? Current.Back : Current.Front);

    /// <summary>The image for the current face, resolved through the package.</summary>
    public byte[]? FaceImage
    {
        get
        {
            var card = Current;
            if (card is null) return null;
            var path = ShowingBack ? card.BackImageRelativePath : card.FrontImageRelativePath;
            return string.IsNullOrEmpty(path) ? null : _session.Package?.GetImage(path);
        }
    }

    public bool HasFaceImage => FaceImage is not null;

    public string RemainingLabel
    {
        get
        {
            var remaining = _due.Count - _index;
            return remaining == 1 ? "1 card left" : $"{remaining} cards left";
        }
    }

    public string Hint => ShowingBack ? "How well did you know it?" : "Tap to reveal the answer";

    /// <summary>Grade buttons only show once the answer is revealed.</summary>
    public bool CanGrade => HasCard && ShowingBack;

    // ----- Commands ---------------------------------------------------------

    [RelayCommand]
    private void Reveal()
    {
        if (!HasCard || ShowingBack) return;
        ShowingBack = true;
        RaiseAll();
    }

    [RelayCommand]
    private void GradeAgain() => Grade(ReviewGrade.Again);

    [RelayCommand]
    private void GradeHard() => Grade(ReviewGrade.Hard);

    [RelayCommand]
    private void GradeGood() => Grade(ReviewGrade.Good);

    [RelayCommand]
    private void GradeEasy() => Grade(ReviewGrade.Easy);

    private void Grade(ReviewGrade grade)
    {
        if (!CanGrade) return;

        var doc = Document;
        if (doc is null) return;

        var card = _due[_index];
        new ReviewSession(_store, doc).Grade(card.Id, grade);  // advance + persist
        _reviewedAny = true;

        _index++;
        ShowingBack = false;
        RaiseAll();
    }

    // Re-derive the computed properties whenever the flip or index changes. The
    // [ObservableProperty] on ShowingBack raises its own change; these are the
    // dependents that the toolkit can't infer.
    partial void OnShowingBackChanged(bool value) => RaiseAll();

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(HasCard));
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(NoStudyCards));
        OnPropertyChanged(nameof(AllCaughtUp));
        OnPropertyChanged(nameof(FaceText));
        OnPropertyChanged(nameof(FaceImage));
        OnPropertyChanged(nameof(HasFaceImage));
        OnPropertyChanged(nameof(RemainingLabel));
        OnPropertyChanged(nameof(Hint));
        OnPropertyChanged(nameof(CanGrade));
    }
}
