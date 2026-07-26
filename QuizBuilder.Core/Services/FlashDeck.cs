using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <summary>
/// One card: a question on the front, its answer on the back.
/// </summary>
public sealed class FlashCard
{
    public FlashCard(int number, Question question)
    {
        Number = number;
        Front = question.Prompt;
        TypeLabel = question.KindDisplayName;
        FrontImageRelativePath = question.ImageRelativePath;

        var answer = AnswerDescriber.Describe(question);

        // An essay has no single answer, so the describer returns empty. Show the
        // author's rubric if there is one, otherwise say plainly that it is open.
        // A blank back would just look like a fault.
        if (string.IsNullOrEmpty(answer))
        {
            IsOpenResponse = question is EssayQuestion;

            Back = question is EssayQuestion { RubricNotes: { Length: > 0 } notes }
                ? notes
                : "Open response — no single correct answer.";
        }
        else
        {
            Back = answer;
            IsOpenResponse = false;
        }
    }

    /// <summary>A card from a hand-authored study card: front and back as written.</summary>
    public FlashCard(int number, StudyCard card)
    {
        Number = number;
        Front = card.Front;
        Back = card.Back;
        TypeLabel = "Study card";
        IsOpenResponse = false;
        FrontImageRelativePath = card.FrontImageRelativePath;
        BackImageRelativePath = card.BackImageRelativePath;
    }

    public int Number { get; }
    public string Front { get; }
    public string Back { get; }
    public string TypeLabel { get; }

    /// <summary>True for an essay, so the back can be styled as guidance, not an answer.</summary>
    public bool IsOpenResponse { get; }

    /// <summary>
    /// Image paths for each side, or null. A question card only ever has a front
    /// image (the prompt's); a study card can have either. The view model turns
    /// these into bytes through the package service.
    /// </summary>
    public string? FrontImageRelativePath { get; }
    public string? BackImageRelativePath { get; }
}

/// <summary>
/// A deck of flash cards with a current position and a flipped/unflipped face.
///
/// This is the whole of the flash-card behaviour, in Core, so it can be tested
/// on Linux like everything else. The WPF view model is a thin wrapper that
/// forwards to a deck and raises change notifications -- there is no logic up
/// there to test that is not tested here.
/// </summary>
public sealed class FlashDeck
{
    private readonly List<FlashCard> _cards;
    private int _index;

    public FlashDeck(IEnumerable<Question> questions)
    {
        ArgumentNullException.ThrowIfNull(questions);

        _cards = questions
            .Select((q, i) => new FlashCard(i + 1, q))
            .ToList();
    }

    private FlashDeck(List<FlashCard> cards)
    {
        _cards = cards;
    }

    /// <summary>
    /// Builds a deck from the document according to the chosen source.
    ///
    /// The numbering runs continuously across both sources when Both is chosen,
    /// so a mixed deck reads "1 of 20" without a gap where the questions end and
    /// the study cards begin. Quiz questions come first in that case, matching
    /// the order in the enum and the toggle.
    /// </summary>
    public static FlashDeck Build(QuizDocument document, FlashCardSource source)
    {
        ArgumentNullException.ThrowIfNull(document);

        var cards = new List<FlashCard>();
        var number = 1;

        if (source is FlashCardSource.Quiz or FlashCardSource.Both)
        {
            foreach (var question in document.SectionsInDisplayOrder().SelectMany(s => s.Questions))
                cards.Add(new FlashCard(number++, question));
        }

        if (source is FlashCardSource.StudyCards or FlashCardSource.Both)
        {
            foreach (var card in document.StudyCards)
                cards.Add(new FlashCard(number++, card));
        }

        return new FlashDeck(cards);
    }

    public bool HasCards => _cards.Count > 0;

    public int Count => _cards.Count;

    public FlashCard? Current => HasCards ? _cards[_index] : null;

    /// <summary>Which face is up.</summary>
    public bool ShowingBack { get; private set; }

    /// <summary>1-based position, e.g. "3 of 20". "No cards" for an empty deck.</summary>
    public string ProgressLabel => HasCards ? $"{_index + 1} of {_cards.Count}" : "No cards";

    public bool CanGoNext => HasCards && _index < _cards.Count - 1;

    public bool CanGoPrevious => HasCards && _index > 0;

    public bool CanShuffle => _cards.Count > 1;

    public void Flip() => ShowingBack = !ShowingBack;

    public void Next()
    {
        if (!CanGoNext) return;

        _index++;

        // Always land on the question side: arriving at a new card already
        // showing its answer defeats the point of a flash card.
        ShowingBack = false;
    }

    public void Previous()
    {
        if (!CanGoPrevious) return;

        _index--;
        ShowingBack = false;
    }

    /// <summary>
    /// Reorders the deck and returns to the first card, question side up.
    /// </summary>
    public void Shuffle(Random random)
    {
        ArgumentNullException.ThrowIfNull(random);

        if (_cards.Count <= 1) return;

        // Fisher-Yates. Not the compiler's seeded shuffle: that one is
        // reproducible on purpose for exports, and a shuffle the user asked for
        // should be different each time.
        for (var i = _cards.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
        }

        _index = 0;
        ShowingBack = false;
    }
}
