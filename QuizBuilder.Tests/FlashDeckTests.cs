using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// The flash deck: card building, navigation bounds, flip, shuffle. This is the
/// whole of the flash-card behaviour; the WPF view model only forwards to it.
/// </summary>
public class FlashDeckTests
{
    private static MultipleChoiceSingleQuestion Q(string prompt, string answer)
    {
        var q = new MultipleChoiceSingleQuestion { Prompt = prompt };
        q.Choices.Add(new Choice { Text = "wrong" });
        q.Choices.Add(new Choice { Text = answer, IsCorrect = true });
        return q;
    }

    private static FlashDeck Deck(params Question[] questions) => new(questions);

    [Fact]
    public void CardsAreBuiltFrontAndBack()
    {
        var deck = Deck(Q("one", "1"), Q("two", "2"));

        Assert.True(deck.HasCards);
        Assert.Equal(2, deck.Count);
        Assert.Equal("one", deck.Current!.Front);
        Assert.Equal("1", deck.Current.Back);
    }

    [Fact]
    public void AnEmptyDeckHasNoCurrentCard()
    {
        var deck = new FlashDeck(System.Array.Empty<Question>());

        Assert.False(deck.HasCards);
        Assert.Null(deck.Current);
        Assert.Equal("No cards", deck.ProgressLabel);
    }

    [Fact]
    public void FlipTogglesTheFace()
    {
        var deck = Deck(Q("q", "a"));

        Assert.False(deck.ShowingBack);
        deck.Flip();
        Assert.True(deck.ShowingBack);
        deck.Flip();
        Assert.False(deck.ShowingBack);
    }

    [Fact]
    public void MovingToTheNextCardShowsItsQuestionSide()
    {
        var deck = Deck(Q("one", "1"), Q("two", "2"));

        deck.Flip();
        Assert.True(deck.ShowingBack);

        deck.Next();

        Assert.False(deck.ShowingBack);
        Assert.Equal("two", deck.Current!.Front);
    }

    [Fact]
    public void NavigationBoundsHold()
    {
        var deck = Deck(Q("one", "1"), Q("two", "2"));

        Assert.False(deck.CanGoPrevious);
        Assert.True(deck.CanGoNext);

        deck.Next();

        Assert.True(deck.CanGoPrevious);
        Assert.False(deck.CanGoNext);
    }

    [Fact]
    public void NextPastTheEndDoesNothing()
    {
        var deck = Deck(Q("only", "1"));

        deck.Next();

        Assert.Equal("1 of 1", deck.ProgressLabel);
    }

    [Fact]
    public void ProgressTracksPosition()
    {
        var deck = Deck(Q("one", "1"), Q("two", "2"), Q("three", "3"));

        Assert.Equal("1 of 3", deck.ProgressLabel);
        deck.Next();
        Assert.Equal("2 of 3", deck.ProgressLabel);
    }

    [Fact]
    public void ShuffleNeedsMoreThanOneCard()
    {
        Assert.False(Deck(Q("only", "1")).CanShuffle);
        Assert.True(Deck(Q("one", "1"), Q("two", "2")).CanShuffle);
    }

    [Fact]
    public void ShuffleReturnsToTheFirstCardQuestionSide()
    {
        var deck = Deck(Q("one", "1"), Q("two", "2"), Q("three", "3"));

        deck.Next();
        deck.Flip();

        deck.Shuffle(new System.Random(1));

        Assert.Equal("1 of 3", deck.ProgressLabel);
        Assert.False(deck.ShowingBack);
    }

    [Fact]
    public void ShuffleKeepsEveryCard()
    {
        var deck = Deck(Q("one", "1"), Q("two", "2"), Q("three", "3"), Q("four", "4"));

        deck.Shuffle(new System.Random(42));

        // Same cards, possibly reordered -- none lost or duplicated.
        var fronts = new System.Collections.Generic.List<string>();
        for (var i = 0; i < deck.Count; i++)
        {
            fronts.Add(deck.Current!.Front);
            deck.Next();
        }

        Assert.Equal(4, fronts.Distinct().Count());
        Assert.Contains("one", fronts);
        Assert.Contains("four", fronts);
    }

    [Fact]
    public void AnEssayCardSaysThereIsNoSingleAnswer()
    {
        var deck = Deck(new EssayQuestion { Prompt = "Discuss." });

        Assert.Contains("Open response", deck.Current!.Back);
        Assert.True(deck.Current.IsOpenResponse);
    }

    [Fact]
    public void AnEssayWithRubricNotesShowsThem()
    {
        var deck = Deck(new EssayQuestion { Prompt = "Discuss.", RubricNotes = "Mention three factors." });

        Assert.Equal("Mention three factors.", deck.Current!.Back);
    }

    [Fact]
    public void CardNumbersAreOneBasedInOrder()
    {
        var deck = Deck(Q("a", "1"), Q("b", "2"));

        Assert.Equal(1, deck.Current!.Number);
        deck.Next();
        Assert.Equal(2, deck.Current!.Number);
    }
}
