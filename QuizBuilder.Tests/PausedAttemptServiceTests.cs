using System;
using System.IO;
using System.Linq;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// The paused-attempt store: round-tripping a saved sitting to disk, per-quiz
/// listing, replace-in-place on re-save, and removal. The snapshot must survive
/// a full save/reload with its paper, answers, and elapsed time intact -- that
/// is the whole point of the feature.
/// </summary>
public class PausedAttemptServiceTests : IDisposable
{
    private readonly string _dir;

    public PausedAttemptServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "qb-paused-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static PausedAttempt Sample(Guid quizId, string title = "Quiz")
    {
        var mc = new MultipleChoiceSingleQuestion { Prompt = "Powerhouse?", Points = 1 };
        mc.Choices.Add(new Choice { Text = "Mitochondria", IsCorrect = true });
        mc.Choices.Add(new Choice { Text = "Nucleus" });

        var matching = new MatchingQuestion { Prompt = "Match", Points = 2 };
        matching.Pairs.Add(new MatchPair { Left = "A", Right = "1" });
        matching.Pairs.Add(new MatchPair { Left = "B", Right = "2" });

        return new PausedAttempt
        {
            QuizId = quizId,
            QuizTitle = title,
            SavedAt = DateTimeOffset.UtcNow,
            ElapsedSeconds = 615,
            TimeLimitMinutes = 30,
            PassPercentage = 50,
            PassOnQuestionCount = true,
            Sections =
            {
                new PausedSection
                {
                    Title = "Cells",
                    Questions =
                    {
                        new PausedQuestion
                        {
                            Number = 1,
                            Question = mc,
                            Answer = new QuestionAnswer { ChoiceIndex = 0 },
                        },
                        new PausedQuestion
                        {
                            Number = 2,
                            Question = matching,
                            MatchingOptions = new() { "2", "1" },   // shuffled order
                            Answer = new QuestionAnswer { MatchAnswers = { [0] = "1" } },
                        },
                    },
                },
            },
        };
    }

    [Fact]
    public void SavedAttemptSurvivesReload()
    {
        var quizId = Guid.NewGuid();
        var attempt = Sample(quizId);

        var service = new PausedAttemptService(_dir);
        service.Save(attempt);

        // A fresh service reads from disk -- nothing served from memory.
        var reloaded = new PausedAttemptService(_dir);
        reloaded.Load();

        var got = reloaded.ForQuiz(quizId).Single();

        Assert.Equal(attempt.Id, got.Id);
        Assert.Equal(615, got.ElapsedSeconds);
        Assert.Equal(30, got.TimeLimitMinutes);
        Assert.Single(got.Sections);
        Assert.Equal(2, got.Sections[0].Questions.Count);
    }

    [Fact]
    public void PolymorphicQuestionsAndAnswersRoundTrip()
    {
        var quizId = Guid.NewGuid();

        var service = new PausedAttemptService(_dir);
        service.Save(Sample(quizId));

        var reloaded = new PausedAttemptService(_dir);
        reloaded.Load();
        var got = reloaded.ForQuiz(quizId).Single();

        var q1 = got.Sections[0].Questions[0];
        var q2 = got.Sections[0].Questions[1];

        // The concrete question types survive the $kind discriminator.
        Assert.IsType<MultipleChoiceSingleQuestion>(q1.Question);
        Assert.IsType<MatchingQuestion>(q2.Question);

        // The partial answers survive.
        Assert.Equal(0, q1.Answer.ChoiceIndex);
        Assert.Equal("1", q2.Answer.MatchAnswers[0]);

        // The shuffled matching order survives -- resume must show what was paused.
        Assert.Equal(new[] { "2", "1" }, q2.MatchingOptions);
    }

    [Fact]
    public void ForQuizReturnsOnlyThatQuizsAttempts()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var service = new PausedAttemptService(_dir);
        service.Save(Sample(a, "A"));
        service.Save(Sample(b, "B"));

        Assert.Single(service.ForQuiz(a));
        Assert.Single(service.ForQuiz(b));
        Assert.Equal("A", service.ForQuiz(a).Single().QuizTitle);
    }

    [Fact]
    public void ResavingTheSameAttemptReplacesItInPlace()
    {
        var quizId = Guid.NewGuid();
        var attempt = Sample(quizId);

        var service = new PausedAttemptService(_dir);
        service.Save(attempt);

        // Same id, more time elapsed.
        attempt.ElapsedSeconds = 900;
        service.Save(attempt);

        var all = service.ForQuiz(quizId);
        Assert.Single(all);                       // not two entries
        Assert.Equal(900, all[0].ElapsedSeconds); // the newer value
    }

    [Fact]
    public void RemoveDeletesTheAttempt()
    {
        var quizId = Guid.NewGuid();
        var attempt = Sample(quizId);

        var service = new PausedAttemptService(_dir);
        service.Save(attempt);
        service.Remove(attempt.Id);

        Assert.Empty(service.ForQuiz(quizId));

        // And it is gone from disk, not just memory.
        var reloaded = new PausedAttemptService(_dir);
        reloaded.Load();
        Assert.Empty(reloaded.ForQuiz(quizId));
    }

    [Fact]
    public void LoadWithNoFileIsEmptyNotAnError()
    {
        var service = new PausedAttemptService(_dir);
        service.Load();   // no file yet

        Assert.Empty(service.ForQuiz(Guid.NewGuid()));
    }

    [Fact]
    public void AttemptsAreListedNewestFirst()
    {
        var quizId = Guid.NewGuid();
        var service = new PausedAttemptService(_dir);

        var older = Sample(quizId);
        older.SavedAt = DateTimeOffset.UtcNow.AddHours(-2);
        var newer = Sample(quizId);
        newer.SavedAt = DateTimeOffset.UtcNow;

        service.Save(older);
        service.Save(newer);

        var all = service.ForQuiz(quizId);
        Assert.Equal(newer.Id, all[0].Id);
        Assert.Equal(older.Id, all[1].Id);
    }
}
