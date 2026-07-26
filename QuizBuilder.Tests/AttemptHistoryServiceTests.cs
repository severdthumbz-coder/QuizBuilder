using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// History is stored beside the executable, keyed on the quiz's Guid -- which
/// survives a .qbx round trip, so reopening a quiz finds its attempts.
/// </summary>
public class AttemptHistoryServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AttemptHistoryService _history;

    public AttemptHistoryServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "qb-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _history = new AttemptHistoryService(_tempDir);
        _history.Load();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private static AttemptRecord Attempt(Guid quizId, double percentage = 80, DateTimeOffset? at = null) => new()
    {
        QuizId = quizId,
        QuizTitle = "T",
        TakenAt = at ?? DateTimeOffset.Now,
        Percentage = percentage,
        Passed = percentage >= 50,
        ScoredPoints = percentage / 10,
        AutoGradedPoints = 10,
        ElapsedSeconds = 65,
    };

    [Fact]
    public void AnAttemptIsFoundByItsQuizId()
    {
        var quizId = Guid.NewGuid();
        _history.Add(Attempt(quizId));

        Assert.Single(_history.ForQuiz(quizId));
        Assert.Empty(_history.ForQuiz(Guid.NewGuid()));
    }

    [Fact]
    public void HistorySurvivesAReload()
    {
        // The whole point: close the app, reopen the quiz, the attempts are there.
        var quizId = Guid.NewGuid();
        _history.Add(Attempt(quizId, 75));

        var reopened = new AttemptHistoryService(_tempDir);
        reopened.Load();

        var found = reopened.ForQuiz(quizId);

        Assert.Single(found);
        Assert.Equal(75, found[0].Percentage);
    }

    [Fact]
    public void ElapsedRoundTripsThroughSeconds()
    {
        // Stored as an int, not a TimeSpan: System.Text.Json's TimeSpan support
        // could not be verified in the environment this was built in, and
        // "probably works" is not a thing to put in a file format.
        var quizId = Guid.NewGuid();

        var attempt = Attempt(quizId);
        attempt.Elapsed = TimeSpan.FromMinutes(2.5);

        _history.Add(attempt);

        var reopened = new AttemptHistoryService(_tempDir);
        reopened.Load();

        Assert.Equal(TimeSpan.FromSeconds(150), reopened.ForQuiz(quizId)[0].Elapsed);
        Assert.Equal(150, reopened.ForQuiz(quizId)[0].ElapsedSeconds);
    }

    [Fact]
    public void AttemptsComeBackNewestFirst()
    {
        var quizId = Guid.NewGuid();
        var now = DateTimeOffset.Now;

        _history.Add(Attempt(quizId, 50, now.AddDays(-2)));
        _history.Add(Attempt(quizId, 90, now));
        _history.Add(Attempt(quizId, 70, now.AddDays(-1)));

        var found = _history.ForQuiz(quizId);

        Assert.Equal(90, found[0].Percentage);
        Assert.Equal(70, found[1].Percentage);
        Assert.Equal(50, found[2].Percentage);
    }

    [Fact]
    public void AnAttemptCanBeForgotten()
    {
        var quizId = Guid.NewGuid();
        var attempt = Attempt(quizId);

        _history.Add(attempt);
        _history.Remove(attempt.Id);

        Assert.Empty(_history.ForQuiz(quizId));
    }

    [Fact]
    public void ClearingOneQuizLeavesTheOthersAlone()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        _history.Add(Attempt(a));
        _history.Add(Attempt(b));

        _history.ClearForQuiz(a);

        Assert.Empty(_history.ForQuiz(a));
        Assert.Single(_history.ForQuiz(b));
    }

    [Fact]
    public void HistoryIsCappedSoItCannotGrowForever()
    {
        // Someone drilling a quiz daily would otherwise grow a file that is read
        // at startup.
        var quizId = Guid.NewGuid();
        var now = DateTimeOffset.Now;

        for (var i = 0; i < 60; i++)
            _history.Add(Attempt(quizId, i, now.AddMinutes(-i)));

        var found = _history.ForQuiz(quizId);

        Assert.Equal(50, found.Count);

        // The oldest fall off, not the newest.
        Assert.Equal(0, found[0].Percentage);
    }

    [Fact]
    public void ACorruptFileDoesNotStopTheAppStarting()
    {
        File.WriteAllText(Path.Combine(_tempDir, "history.json"), "{ this is not json");

        var service = new AttemptHistoryService(_tempDir);
        service.Load();

        // Losing past scores is a nuisance. Refusing to open the app is not.
        Assert.Empty(service.ForQuiz(Guid.NewGuid()));
    }

    [Fact]
    public void AMissingFileIsTheNormalFirstRun()
    {
        var service = new AttemptHistoryService(Path.Combine(_tempDir, "does-not-exist"));
        service.Load();

        Assert.Empty(service.ForQuiz(Guid.NewGuid()));
    }

    [Fact]
    public void AddingRaisesHistoryChanged()
    {
        var raised = 0;
        _history.HistoryChanged += (_, _) => raised++;

        _history.Add(Attempt(Guid.NewGuid()));

        Assert.Equal(1, raised);
    }

    [Fact]
    public void RemovingSomethingThatIsNotThereRaisesNothing()
    {
        var raised = 0;
        _history.HistoryChanged += (_, _) => raised++;

        _history.Remove(Guid.NewGuid());

        Assert.Equal(0, raised);
    }
}
