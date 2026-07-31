using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;
using QuizBuilder.Player.Services;

namespace QuizBuilder.Player.ViewModels;

/// <summary>
/// Review mode: flip through the loaded quiz as flash cards. All the deck
/// behaviour -- ordering, flip, next/prev, shuffle -- lives in Core's
/// <see cref="FlashDeck"/>, which is unit-tested on Linux. This VM is the thin
/// wrapper the HANDOFF describes: it forwards to a deck and raises change
/// notifications, resolves card images through the package service, and lets the
/// taker pick the card source. There is no review logic here that is not proven
/// in Core.
/// </summary>
public partial class StudyCardsViewModel : ObservableObject
{
    private readonly QuizSessionService _session;
    private readonly Random _random = new();

    private FlashDeck _deck;

    public StudyCardsViewModel(QuizSessionService session)
    {
        _session = session;

        // Which sources are even offered depends on what the document holds. A
        // quiz with no study cards should not present a StudyCards/Both toggle
        // that would build an empty deck; a study-card-only document is unusual
        // but handled symmetrically.
        var doc = _session.Loaded?.Document;
        HasStudyCards = doc is not null && doc.StudyCards.Count > 0;
        HasQuestions = doc is not null && doc.QuestionCount > 0;

        // Default to whatever gives the taker the most to review without a
        // surprising empty state: Both when cards exist, else plain Quiz.
        _source = HasStudyCards ? FlashCardSource.Both : FlashCardSource.Quiz;

        _deck = BuildDeck();
    }

    // ----- Source toggle -----------------------------------------------------

    public bool HasStudyCards { get; }
    public bool HasQuestions { get; }

    /// <summary>Show the source toggle only when there is a real choice to make.</summary>
    public bool CanChooseSource => HasStudyCards && HasQuestions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQuizSource))]
    [NotifyPropertyChangedFor(nameof(IsStudyCardsSource))]
    [NotifyPropertyChangedFor(nameof(IsBothSource))]
    private FlashCardSource _source;

    // Bindable one-way mirrors for a segmented control / radio-style UI.
    public bool IsQuizSource => Source == FlashCardSource.Quiz;
    public bool IsStudyCardsSource => Source == FlashCardSource.StudyCards;
    public bool IsBothSource => Source == FlashCardSource.Both;

    [RelayCommand]
    private void SetSource(string which)
    {
        var chosen = which switch
        {
            "quiz" => FlashCardSource.Quiz,
            "cards" => FlashCardSource.StudyCards,
            _ => FlashCardSource.Both,
        };

        if (chosen == Source) return;

        Source = chosen;
        _deck = BuildDeck();
        RaiseDeckChanged();
    }

    // ----- Current card ------------------------------------------------------

    public bool HasCards => _deck.HasCards;

    /// <summary>What is written on the face currently showing.</summary>
    public string FaceText
    {
        get
        {
            var card = _deck.Current;
            if (card is null) return string.Empty;
            return _deck.ShowingBack ? card.Back : card.Front;
        }
    }

    /// <summary>Image bytes for the face currently showing, or null. Resolved
    /// through the package so the byte[]-to-ImageSource converter can render it,
    /// exactly as the take screen resolves question images.</summary>
    public byte[]? FaceImage
    {
        get
        {
            var card = _deck.Current;
            if (card is null) return null;

            var path = _deck.ShowingBack ? card.BackImageRelativePath : card.FrontImageRelativePath;
            return _session.Package?.GetImage(path);
        }
    }

    /// <summary>"Question" / "Answer" label above the face, so the taker always
    /// knows which side they are looking at. For an open-response card the back
    /// is guidance, not an answer, so it is labelled accordingly.</summary>
    public string FaceLabel
    {
        get
        {
            if (!_deck.ShowingBack) return "Question";
            return _deck.Current?.IsOpenResponse == true ? "Guidance" : "Answer";
        }
    }

    /// <summary>The card's type, e.g. "Multiple choice" or "Study card".</summary>
    public string TypeLabel => _deck.Current?.TypeLabel ?? string.Empty;

    public string ProgressLabel => _deck.ProgressLabel;

    public bool ShowingBack => _deck.ShowingBack;

    /// <summary>Prompt on the flip button reflects what tapping will do.</summary>
    public string FlipButtonText => _deck.ShowingBack ? "Show question" : "Show answer";

    public bool CanGoNext => _deck.CanGoNext;
    public bool CanGoPrevious => _deck.CanGoPrevious;
    public bool CanShuffle => _deck.CanShuffle;

    // ----- Commands ----------------------------------------------------------

    [RelayCommand]
    private void Flip()
    {
        _deck.Flip();
        RaiseFaceChanged();
    }

    [RelayCommand]
    private void Next()
    {
        _deck.Next();
        RaiseDeckChanged();
    }

    [RelayCommand]
    private void Previous()
    {
        _deck.Previous();
        RaiseDeckChanged();
    }

    [RelayCommand]
    private void Shuffle()
    {
        _deck.Shuffle(_random);
        RaiseDeckChanged();
    }

    // ----- Plumbing ----------------------------------------------------------

    private FlashDeck BuildDeck()
    {
        var doc = _session.Loaded?.Document;
        return doc is null
            ? new FlashDeck(Enumerable.Empty<Core.Models.Question>())
            : FlashDeck.Build(doc, Source);
    }

    /// <summary>Face-only change (a flip): the text, image and the two labels move.</summary>
    private void RaiseFaceChanged()
    {
        OnPropertyChanged(nameof(FaceText));
        OnPropertyChanged(nameof(FaceImage));
        OnPropertyChanged(nameof(FaceLabel));
        OnPropertyChanged(nameof(ShowingBack));
        OnPropertyChanged(nameof(FlipButtonText));
    }

    /// <summary>A card move or deck rebuild: everything the face shows, plus
    /// position, type and the nav/shuffle enabled states. Button enablement is
    /// bound to the Can* properties in XAML rather than command CanExecute, to
    /// avoid the stale-CanExecute timing the HomeViewModel note warns about.</summary>
    private void RaiseDeckChanged()
    {
        RaiseFaceChanged();
        OnPropertyChanged(nameof(HasCards));
        OnPropertyChanged(nameof(TypeLabel));
        OnPropertyChanged(nameof(ProgressLabel));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanShuffle));
    }
}
