using System.Linq;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;

namespace QuizBuilder.App.ViewModels;

/// <summary>
/// The Flash Cards tab: a thin WPF wrapper over <see cref="FlashDeck"/>.
///
/// All the behaviour -- building cards, navigation, flip, shuffle -- lives in
/// the Core deck, which is tested on Linux. This layer forwards commands to it
/// and raises change notifications. Keeping it thin is deliberate: there is
/// nothing here that could break which is not covered by a Core test.
/// </summary>
public sealed class FlashCardsViewModel : ViewModelBase
{
    private readonly IQuizDocumentService _document;
    private readonly ISettingsService _settings;
    private readonly IQuizPackageService _images;
    private readonly IThemeService _theme;

    private FlashDeck _deck = new(System.Array.Empty<Core.Models.Question>());
    private bool _isVisible;
    private bool _isStale = true;

    public FlashCardsViewModel(
        IQuizDocumentService document,
        ISettingsService settings,
        IQuizPackageService images,
        IThemeService theme)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _images = images ?? throw new ArgumentNullException(nameof(images));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));

        // Switching theme changes the base type ramp the multiplier sits on.
        _theme.ThemeChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CardFontSize));
            OnPropertyChanged(nameof(CardCaptionSize));
        };

        // Rebuild when the document changes (cards added/edited) OR when settings
        // change (the source toggle). Both can alter what the deck should hold.
        _document.DocumentChanged += (_, _) => MarkStale();
        _settings.SettingsChanged += (_, _) => MarkStale();

        FlipCommand = new RelayCommand(Flip, () => _deck.HasCards);
        NextCommand = new RelayCommand(Next, () => _deck.CanGoNext);
        PreviousCommand = new RelayCommand(Previous, () => _deck.CanGoPrevious);
        ShuffleCommand = new RelayCommand(Shuffle, () => _deck.CanShuffle);

        BiggerTextCommand = new RelayCommand(
            () => AdjustTextScale(+QuizSettings.FlashCardTextScaleStep),
            () => TextScale < QuizSettings.FlashCardTextScaleMax);

        SmallerTextCommand = new RelayCommand(
            () => AdjustTextScale(-QuizSettings.FlashCardTextScaleStep),
            () => TextScale > QuizSettings.FlashCardTextScaleMin);
    }

    public RelayCommand FlipCommand { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand PreviousCommand { get; }
    public RelayCommand ShuffleCommand { get; }

    public RelayCommand BiggerTextCommand { get; }
    public RelayCommand SmallerTextCommand { get; }

    /// <summary>
    /// The saved multiplier, clamped on read. Reading through a clamp means a
    /// hand-edited settings file cannot produce unreadable or comically large
    /// cards.
    /// </summary>
    public double TextScale => Math.Clamp(
        _settings.Current.Quiz.FlashCardTextScale,
        QuizSettings.FlashCardTextScaleMin,
        QuizSettings.FlashCardTextScaleMax);

    /// <summary>
    /// The card's text size in DIP: the theme's subtitle size scaled by the
    /// user's multiplier. Derived from the theme rather than stored, so
    /// switching theme still moves the cards with it.
    /// </summary>
    public double CardFontSize => Math.Round(_theme.Current.Typography.Subtitle * TextScale);

    /// <summary>Caption text scales too, or the labels look lost on a big card.</summary>
    public double CardCaptionSize => Math.Round(_theme.Current.Typography.Caption * TextScale);

    /// <summary>Shown next to the size buttons, e.g. "150%".</summary>
    public string TextScaleLabel =>
        (TextScale * 100).ToString("0", System.Globalization.CultureInfo.CurrentCulture) + "%";

    private void AdjustTextScale(double delta)
    {
        var next = Math.Clamp(
            TextScale + delta,
            QuizSettings.FlashCardTextScaleMin,
            QuizSettings.FlashCardTextScaleMax);

        if (Math.Abs(next - TextScale) < 0.001) return;

        _settings.Current.Quiz.FlashCardTextScale = next;
        _settings.Save();

        // Only the text metrics changed. Raising these directly rather than
        // going through MarkStale avoids rebuilding the deck and losing the
        // card the user is looking at.
        OnPropertyChanged(nameof(TextScale));
        OnPropertyChanged(nameof(CardFontSize));
        OnPropertyChanged(nameof(CardCaptionSize));
        OnPropertyChanged(nameof(TextScaleLabel));
        RelayCommand.RaiseCanExecuteChanged();
    }

    public bool HasCards => _deck.HasCards;
    public FlashCard? Current => _deck.Current;
    public bool ShowingBack => _deck.ShowingBack;
    public string ProgressLabel => _deck.ProgressLabel;

    /// <summary>
    /// The image for the face currently showing, or null. Resolved here so the
    /// view binds one property that follows the flip, rather than juggling front
    /// and back images itself.
    /// </summary>
    public byte[]? CurrentImageBytes
    {
        get
        {
            var path = _deck.ShowingBack ? _deck.Current?.BackImageRelativePath : _deck.Current?.FrontImageRelativePath;
            return string.IsNullOrEmpty(path) ? null : _images.GetImage(path);
        }
    }

    public bool HasCurrentImage => CurrentImageBytes is not null;

    public string FlipHint => _deck.ShowingBack
        ? "Showing the answer — click to see the question"
        : "Click the card to reveal the answer";

    public string EmptyMessage => "Add some questions to this quiz and they'll appear here as flash cards.";

    private void MarkStale()
    {
        if (_isVisible) Rebuild();
        else _isStale = true;
    }

    public void OnActivated()
    {
        _isVisible = true;
        if (_isStale) Rebuild();
    }

    public void OnDeactivated() => _isVisible = false;

    private void Rebuild()
    {
        _isStale = false;

        // Document order, not shuffled: a freshly opened deck should track the
        // quiz. Shuffle is a deliberate button.
        _deck = FlashDeck.Build(_document.Current, _settings.Current.Quiz.FlashCardSource);

        RaiseAll();
    }

    private void Flip()
    {
        _deck.Flip();
        RaiseFace();
    }

    private void Next()
    {
        _deck.Next();
        RaiseCard();
    }

    private void Previous()
    {
        _deck.Previous();
        RaiseCard();
    }

    private void Shuffle()
    {
        _deck.Shuffle(System.Random.Shared);
        RaiseAll();
    }

    private void RaiseFace()
    {
        OnPropertyChanged(nameof(ShowingBack));
        OnPropertyChanged(nameof(FlipHint));

        // The image follows the face, so it changes on every flip as well as
        // every card change (RaiseCard calls through here).
        OnPropertyChanged(nameof(CurrentImageBytes));
        OnPropertyChanged(nameof(HasCurrentImage));
    }

    private void RaiseCard()
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(ProgressLabel));
        RaiseFace();

        RelayCommand.RaiseCanExecuteChanged();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(HasCards));
        OnPropertyChanged(nameof(EmptyMessage));
        RaiseCard();
    }
}
