using System;
using System.Linq;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Rebuilding the in-memory paper from a paused snapshot. Resume depends on this
/// producing exactly what was paused -- same questions, same order, same shuffled
/// matching options -- and on the answers coming back in paper order.
/// </summary>
public class PausedAttemptPaperTests
{
    private static PausedAttempt Sample()
    {
        var mc = new MultipleChoiceSingleQuestion { Prompt = "Powerhouse?", Points = 1 };
        mc.Choices.Add(new Choice { Text = "Mitochondria", IsCorrect = true });
        mc.Choices.Add(new Choice { Text = "Nucleus" });

        var matching = new MatchingQuestion { Prompt = "Match", Points = 2 };
        matching.Pairs.Add(new MatchPair { Left = "A", Right = "1" });
        matching.Pairs.Add(new MatchPair { Left = "B", Right = "2" });

        return new PausedAttempt
        {
            QuizId = Guid.NewGuid(),
            QuizTitle = "Biology",
            SavedAt = DateTimeOffset.Now,
            ElapsedSeconds = 300,
            TimeLimitMinutes = 25,
            Sections =
            {
                new PausedSection
                {
                    SourceSectionId = Guid.NewGuid(),
                    Title = "Cells",
                    Questions =
                    {
                        new PausedQuestion
                        {
                            Number = 1, Question = mc,
                            Answer = new QuestionAnswer { ChoiceIndex = 0 },
                        },
                        new PausedQuestion
                        {
                            Number = 2, Question = matching,
                            MatchingOptions = new() { "2", "1" },
                            Answer = new QuestionAnswer { MatchAnswers = { [0] = "1" } },
                        },
                    },
                },
            },
        };
    }

    [Fact]
    public void RebuildsThePaperWithTitleAndTimeLimit()
    {
        var quiz = PausedAttemptPaper.ToCompiledQuiz(Sample());

        Assert.Equal("Biology", quiz.Title);
        Assert.Equal(25, quiz.TimeLimitMinutes);
        Assert.Single(quiz.Sections);
        Assert.Equal(2, quiz.Sections[0].Questions.Count);
    }

    [Fact]
    public void PreservesQuestionOrderNumbersAndTypes()
    {
        var quiz = PausedAttemptPaper.ToCompiledQuiz(Sample());
        var questions = quiz.Sections[0].Questions;

        Assert.Equal(1, questions[0].Number);
        Assert.Equal(2, questions[1].Number);
        Assert.IsType<MultipleChoiceSingleQuestion>(questions[0].Question);
        Assert.IsType<MatchingQuestion>(questions[1].Question);
    }

    [Fact]
    public void PreservesTheShuffledMatchingOptionOrder()
    {
        var quiz = PausedAttemptPaper.ToCompiledQuiz(Sample());

        // The order the taker was working against must reappear, not the pairs'
        // authoring order.
        Assert.Equal(new[] { "2", "1" }, quiz.Sections[0].Questions[1].MatchingOptions);
    }

    [Fact]
    public void ReturnsAnswersInPaperOrder()
    {
        var answers = PausedAttemptPaper.Answers(Sample());

        Assert.Equal(2, answers.Count);
        Assert.Equal(0, answers[0].ChoiceIndex);          // q1
        Assert.Equal("1", answers[1].MatchAnswers[0]);    // q2
    }

    [Fact]
    public void AnswerOrderMatchesTheFlattenedQuestionOrder()
    {
        // The compiled paper's flattened questions and the answers list must line
        // up index-for-index, or resume drops answers onto the wrong questions.
        var attempt = Sample();

        var quiz = PausedAttemptPaper.ToCompiledQuiz(attempt);
        var answers = PausedAttemptPaper.Answers(attempt);

        var flat = quiz.Sections.SelectMany(s => s.Questions).ToList();
        Assert.Equal(flat.Count, answers.Count);

        // q1 is the MC with ChoiceIndex 0; its answer sits at the same index.
        Assert.Equal(0, answers[0].ChoiceIndex);
        Assert.IsType<MultipleChoiceSingleQuestion>(flat[0].Question);
    }
}
