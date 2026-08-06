using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Spaced repetition (SM-2). The scheduling maths was modelled and proven in
/// tools/port/sm2_scheduler_port.py first; these pin the same behaviour through
/// the real C# Sm2Scheduler, plus the progress store round-trip and the review
/// session's due-queue.
/// </summary>
public class SpacedRepetitionTests
{
    private static ReviewState New() => new() { QuizId = Guid.NewGuid(), CardId = Guid.NewGuid() };

    // ----- Scheduler ------------------------------------------------------ //

    [Fact]
    public void NewCardIsDueImmediately()
    {
        Assert.True(Sm2Scheduler.IsDue(New(), today: 0));
        Assert.True(Sm2Scheduler.IsDue(New(), today: 10_000));
    }

    [Fact]
    public void GoodReviewsProgressOneThenSixThenIntervalTimesEase()
    {
        var s = New();
        s = Sm2Scheduler.Review(s, ReviewGrade.Good, today: 0);
        Assert.Equal(1, s.IntervalDays);
        Assert.Equal(1, s.Repetitions);

        s = Sm2Scheduler.Review(s, ReviewGrade.Good, today: 1);
        Assert.Equal(6, s.IntervalDays);
        Assert.Equal(2, s.Repetitions);

        var easeAfterTwo = s.Ease;
        s = Sm2Scheduler.Review(s, ReviewGrade.Good, today: 7);
        Assert.Equal((int)Math.Round(6 * easeAfterTwo, MidpointRounding.AwayFromZero), s.IntervalDays);
        Assert.Equal(3, s.Repetitions);
    }

    [Fact]
    public void EaseRisesOnEasyFallsOnHard()
    {
        Assert.True(Sm2Scheduler.UpdateEase(2.5, Sm2Scheduler.QualityOf(ReviewGrade.Easy)) > 2.5);
        Assert.True(Sm2Scheduler.UpdateEase(2.5, Sm2Scheduler.QualityOf(ReviewGrade.Hard)) < 2.5);
    }

    [Fact]
    public void EaseNeverFallsBelowFloor()
    {
        var ease = Sm2Defaults.MinEase;
        for (var i = 0; i < 20; i++)
            ease = Sm2Scheduler.UpdateEase(ease, Sm2Scheduler.QualityOf(ReviewGrade.Hard));
        Assert.Equal(Sm2Defaults.MinEase, ease);

        Assert.Equal(Sm2Defaults.MinEase, Sm2Scheduler.UpdateEase(1.3, 0));
    }

    [Fact]
    public void LapseResetsRepsAndIntervalAndLowersEase()
    {
        var s = New();
        s = Sm2Scheduler.Review(s, ReviewGrade.Good, today: 0);
        s = Sm2Scheduler.Review(s, ReviewGrade.Good, today: 1);
        s = Sm2Scheduler.Review(s, ReviewGrade.Easy, today: 7);
        Assert.Equal(3, s.Repetitions);
        Assert.True(s.IntervalDays > 6);
        var easeBefore = s.Ease;

        s = Sm2Scheduler.Review(s, ReviewGrade.Again, today: 20);
        Assert.Equal(0, s.Repetitions);
        Assert.Equal(1, s.IntervalDays);
        Assert.True(s.Ease < easeBefore);
        Assert.True(s.Ease >= Sm2Defaults.MinEase);
    }

    [Fact]
    public void DueExactlyWhenIntervalElapsed()
    {
        var s = Sm2Scheduler.Review(New(), ReviewGrade.Good, today: 0); // interval 1, day 0
        Assert.False(Sm2Scheduler.IsDue(s, today: 0));
        Assert.True(Sm2Scheduler.IsDue(s, today: 1));

        s = Sm2Scheduler.Review(s, ReviewGrade.Good, today: 1);          // interval 6, day 1
        Assert.False(Sm2Scheduler.IsDue(s, today: 6));
        Assert.True(Sm2Scheduler.IsDue(s, today: 7));
    }

    [Fact]
    public void DueQueuePutsNewFirstThenMostOverdue()
    {
        var quiz = Guid.NewGuid();
        var neu = new ReviewState { QuizId = quiz, CardId = Guid.NewGuid() };
        var overdue5 = new ReviewState { QuizId = quiz, CardId = Guid.NewGuid(), Repetitions = 1, Ease = 2.5, IntervalDays = 3, LastReviewedDay = 0 };
        var overdue1 = new ReviewState { QuizId = quiz, CardId = Guid.NewGuid(), Repetitions = 1, Ease = 2.5, IntervalDays = 3, LastReviewedDay = 4 };
        var future = new ReviewState { QuizId = quiz, CardId = Guid.NewGuid(), Repetitions = 2, Ease = 2.5, IntervalDays = 30, LastReviewedDay = 5 };

        var order = Sm2Scheduler.DueQueue(new[] { overdue1, future, neu, overdue5 }, today: 8);

        Assert.DoesNotContain(future, order);
        Assert.Same(neu, order[0]);
        Assert.True(order.ToList().IndexOf(overdue5) < order.ToList().IndexOf(overdue1));
    }

    // ----- Progress store round-trip -------------------------------------- //

    [Fact]
    public void StoreRoundTripsAndScopesByQuizAndCard()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var quiz = Guid.NewGuid();
            var card = Guid.NewGuid();

            var store = new ReviewProgressStore(dir);
            Assert.Null(store.Get(quiz, card));

            store.Save(new ReviewState { QuizId = quiz, CardId = card, Repetitions = 2, Ease = 2.6, IntervalDays = 6, LastReviewedDay = 100 });

            // A fresh instance reads the persisted file.
            var reopened = new ReviewProgressStore(dir);
            var got = reopened.Get(quiz, card);
            Assert.NotNull(got);
            Assert.Equal(2, got!.Repetitions);
            Assert.Equal(6, got.IntervalDays);
            Assert.Equal(100, got.LastReviewedDay);

            // A different card in the same quiz is independent.
            Assert.Null(reopened.Get(quiz, Guid.NewGuid()));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void SavingSameCardReplacesInPlace()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var quiz = Guid.NewGuid();
            var card = Guid.NewGuid();
            var store = new ReviewProgressStore(dir);

            store.Save(new ReviewState { QuizId = quiz, CardId = card, IntervalDays = 1 });
            store.Save(new ReviewState { QuizId = quiz, CardId = card, IntervalDays = 6 });

            Assert.Single(store.ForQuiz(quiz));
            Assert.Equal(6, store.Get(quiz, card)!.IntervalDays);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ClearQuizForgetsOnlyThatQuiz()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var a = Guid.NewGuid();
            var b = Guid.NewGuid();
            var store = new ReviewProgressStore(dir);
            store.Save(new ReviewState { QuizId = a, CardId = Guid.NewGuid() });
            store.Save(new ReviewState { QuizId = b, CardId = Guid.NewGuid() });

            store.ClearQuiz(a);

            Assert.Empty(store.ForQuiz(a));
            Assert.Single(store.ForQuiz(b));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CorruptFileIsToleratedAndStartsFresh()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "review-progress.json"), "{ not valid json");
            var store = new ReviewProgressStore(dir);
            Assert.Empty(store.ForQuiz(Guid.NewGuid()));   // no throw

            var quiz = Guid.NewGuid();
            var card = Guid.NewGuid();
            store.Save(new ReviewState { QuizId = quiz, CardId = card });
            Assert.NotNull(store.Get(quiz, card));         // recovers and writes cleanly
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ----- Review session ------------------------------------------------- //

    private static QuizDocument QuizWithCards(int count)
    {
        var doc = new QuizDocument { Title = "Deck" };
        for (var i = 0; i < count; i++)
            doc.StudyCards.Add(new StudyCard { Front = $"Q{i}", Back = $"A{i}" });
        return doc;
    }

    [Fact]
    public void SessionPresentsAllCardsWhenNoneReviewed()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var doc = QuizWithCards(3);
            var session = new ReviewSession(new ReviewProgressStore(dir), doc);
            Assert.Equal(3, session.DueCount(today: 0));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GradingACardAdvancesItsScheduleAndRemovesItFromToday()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var doc = QuizWithCards(2);
            var store = new ReviewProgressStore(dir);
            var session = new ReviewSession(store, doc);
            var card = doc.StudyCards[0].Id;

            var state = session.Grade(card, ReviewGrade.Good, today: 0);
            Assert.NotNull(state);
            Assert.Equal(1, state!.IntervalDays);

            // Same day, the graded card is no longer due; the other still is.
            var dueToday = session.DueCards(today: 0);
            Assert.DoesNotContain(dueToday, c => c.Id == card);
            Assert.Single(dueToday);

            // Tomorrow it returns.
            Assert.Contains(session.DueCards(today: 1), c => c.Id == card);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GradingAnUnknownCardIsIgnored()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var session = new ReviewSession(new ReviewProgressStore(dir), QuizWithCards(1));
            Assert.Null(session.Grade(Guid.NewGuid(), ReviewGrade.Good, today: 0));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
