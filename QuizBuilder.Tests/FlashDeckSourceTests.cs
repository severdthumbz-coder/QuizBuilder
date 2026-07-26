using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// FlashDeck.Build: assembling a deck from the quiz, the study cards, or both,
/// per the source setting. Numbering runs continuously across sources.
/// </summary>
public class FlashDeckSourceTests
{
    private static MultipleChoiceSingleQuestion Q(string prompt)
    {
        var q = new MultipleChoiceSingleQuestion { Prompt = prompt };
        q.Choices.Add(new Choice { Text = "wrong" });
        q.Choices.Add(new Choice { Text = "right", IsCorrect = true });
        return q;
    }

    private static QuizDocument Doc(string[] questions, (string front, string back)[] studyCards)
    {
        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "S" };
        foreach (var p in questions) section.Questions.Add(Q(p));
        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);

        foreach (var (front, back) in studyCards)
            doc.StudyCards.Add(new StudyCard { Front = front, Back = back });

        return doc;
    }

    [Fact]
    public void QuizSourceUsesOnlyQuestions()
    {
        var doc = Doc(new[] { "q1", "q2" }, new[] { ("s1", "a1") });

        var deck = FlashDeck.Build(doc, FlashCardSource.Quiz);

        Assert.Equal(2, deck.Count);
        Assert.Equal("q1", deck.Current!.Front);
    }

    [Fact]
    public void StudyCardsSourceUsesOnlyStudyCards()
    {
        var doc = Doc(new[] { "q1", "q2" }, new[] { ("Term", "Definition"), ("Cat", "Feline") });

        var deck = FlashDeck.Build(doc, FlashCardSource.StudyCards);

        Assert.Equal(2, deck.Count);
        Assert.Equal("Term", deck.Current!.Front);
        Assert.Equal("Definition", deck.Current.Back);
        Assert.Equal("Study card", deck.Current.TypeLabel);
    }

    [Fact]
    public void BothSourcePutsQuestionsThenStudyCards()
    {
        var doc = Doc(new[] { "q1", "q2" }, new[] { ("s1", "a1"), ("s2", "a2") });

        var deck = FlashDeck.Build(doc, FlashCardSource.Both);

        Assert.Equal(4, deck.Count);
        Assert.Equal("q1", deck.Current!.Front);   // first question

        deck.Next(); deck.Next();                    // to third card
        Assert.Equal("s1", deck.Current!.Front);     // first study card
    }

    [Fact]
    public void NumberingIsContinuousAcrossBothSources()
    {
        var doc = Doc(new[] { "q1", "q2" }, new[] { ("s1", "a1") });

        var deck = FlashDeck.Build(doc, FlashCardSource.Both);

        var numbers = new System.Collections.Generic.List<int>();
        for (var i = 0; i < deck.Count; i++)
        {
            numbers.Add(deck.Current!.Number);
            deck.Next();
        }

        Assert.Equal(new[] { 1, 2, 3 }, numbers);   // no gap between sources
    }

    [Fact]
    public void QuizSourceWithNoQuestionsIsEmpty()
    {
        var doc = Doc(System.Array.Empty<string>(), new[] { ("s1", "a1") });

        Assert.False(FlashDeck.Build(doc, FlashCardSource.Quiz).HasCards);
    }

    [Fact]
    public void StudyCardsSourceWithNoStudyCardsIsEmpty()
    {
        var doc = Doc(new[] { "q1" }, System.Array.Empty<(string, string)>());

        Assert.False(FlashDeck.Build(doc, FlashCardSource.StudyCards).HasCards);
    }

    [Fact]
    public void BothWithOnlyQuestionsFallsBackToQuestions()
    {
        var doc = Doc(new[] { "q1", "q2" }, System.Array.Empty<(string, string)>());

        var deck = FlashDeck.Build(doc, FlashCardSource.Both);

        Assert.Equal(2, deck.Count);
    }

    [Fact]
    public void BothWithNothingIsEmpty()
    {
        var doc = Doc(System.Array.Empty<string>(), System.Array.Empty<(string, string)>());

        Assert.False(FlashDeck.Build(doc, FlashCardSource.Both).HasCards);
    }

    [Fact]
    public void StudyCardImagePathsFlowIntoTheFlashCard()
    {
        var doc = new QuizDocument { Title = "T" };
        doc.StudyCards.Add(new StudyCard
        {
            Front = "front", Back = "back",
            FrontImageRelativePath = "images/aaa.png",
            BackImageRelativePath = "images/bbb.png",
        });

        var deck = FlashDeck.Build(doc, FlashCardSource.StudyCards);

        Assert.Equal("images/aaa.png", deck.Current!.FrontImageRelativePath);
        Assert.Equal("images/bbb.png", deck.Current.BackImageRelativePath);
    }

    [Fact]
    public void AQuestionImageBecomesTheFlashCardFrontImage()
    {
        var doc = Doc(System.Array.Empty<string>(), System.Array.Empty<(string, string)>());
        var section = doc.Sections[0];
        var q = Q("identify this");
        q.ImageRelativePath = "images/diagram.png";
        section.Questions.Add(q);

        var deck = FlashDeck.Build(doc, FlashCardSource.Quiz);

        Assert.Equal("images/diagram.png", deck.Current!.FrontImageRelativePath);
        Assert.Null(deck.Current.BackImageRelativePath);   // an answer describer has no image
    }

}
