using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// The shared answer describer. Used by both the results report and the flash
/// cards, so these tests guard the one place either could drift from.
/// </summary>
public class AnswerDescriberTests
{
    [Fact]
    public void SingleChoiceIsTheCorrectOption()
    {
        var q = new MultipleChoiceSingleQuestion { Prompt = "?" };
        q.Choices.Add(new Choice { Text = "wrong" });
        q.Choices.Add(new Choice { Text = "right", IsCorrect = true });

        Assert.Equal("right", AnswerDescriber.Describe(q));
    }

    [Fact]
    public void MultipleChoiceListsEveryCorrectOption()
    {
        var q = new MultipleChoiceMultipleQuestion { Prompt = "?" };
        q.Choices.Add(new Choice { Text = "a", IsCorrect = true });
        q.Choices.Add(new Choice { Text = "b" });
        q.Choices.Add(new Choice { Text = "c", IsCorrect = true });

        Assert.Equal("a, c", AnswerDescriber.Describe(q));
    }

    [Fact]
    public void TrueFalseReadsTrueOrFalse()
    {
        Assert.Equal("True", AnswerDescriber.Describe(new TrueFalseQuestion { Prompt = "?", CorrectAnswer = true }));
        Assert.Equal("False", AnswerDescriber.Describe(new TrueFalseQuestion { Prompt = "?", CorrectAnswer = false }));
    }

    [Fact]
    public void ShortAnswerJoinsAcceptedAnswers()
    {
        var q = new ShortAnswerQuestion { Prompt = "?" };
        q.AcceptedAnswers.Add("Paris");
        q.AcceptedAnswers.Add("Paris, France");

        Assert.Equal("Paris / Paris, France", AnswerDescriber.Describe(q));
    }

    [Fact]
    public void BlanksAreNumberedInOrdinalOrder()
    {
        var q = new FillInTheBlankQuestion { Prompt = "?" };
        q.Blanks.Add(new Blank { Ordinal = 2, AcceptedAnswers = { "black" } });
        q.Blanks.Add(new Blank { Ordinal = 1, AcceptedAnswers = { "cat", "feline" } });

        Assert.Equal("1: cat / feline, 2: black", AnswerDescriber.Describe(q));
    }

    [Fact]
    public void MatchingShowsEachPair()
    {
        var q = new MatchingQuestion { Prompt = "?" };
        q.Pairs.Add(new MatchPair { Left = "One", Right = "Uno" });
        q.Pairs.Add(new MatchPair { Left = "Two", Right = "Dos" });

        Assert.Equal("One → Uno, Two → Dos", AnswerDescriber.Describe(q));
    }

    [Fact]
    public void AnEssayHasNoDescribedAnswer()
    {
        // Empty on purpose: there is no single answer, and inventing one is the
        // whole reason essays are excluded from grading. Callers decide what to
        // show instead.
        Assert.Equal(string.Empty, AnswerDescriber.Describe(new EssayQuestion { Prompt = "Discuss" }));
    }
}
