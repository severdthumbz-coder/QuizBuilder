using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// "Practice only my wrong answers" (WrongAnswerSelector) and per-quiz
/// statistics (QuizStatisticsService), plus the QuestionId that lets a stored
/// attempt map back to a live question after a prompt edit.
///
/// The selection rule and the aggregate edge cases were modelled in
/// tools/port/wrong_answers_stats_port.py first; these pin the same behaviour
/// through the real C#.
/// </summary>
public class WrongAnswersAndStatsTests
{
    // ----- builders -------------------------------------------------------- //

    private static (QuizDocument Doc, List<Question> Questions) QuizWith(params string[] prompts)
    {
        var doc = new QuizDocument { Title = "Q" };
        var section = new Section { Title = "S" };
        var questions = new List<Question>();

        foreach (var prompt in prompts)
        {
            var q = new ShortAnswerQuestion { Prompt = prompt };
            q.AcceptedAnswers.Add("x");
            section.Questions.Add(q);
            questions.Add(q);
        }

        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);
        return (doc, questions);
    }

    private static AttemptRecord AttemptOn(
        DateTimeOffset at, params (Question Question, bool? Correct)[] verdicts)
    {
        var rec = new AttemptRecord { QuizId = Guid.NewGuid(), TakenAt = at };
        var n = 1;
        foreach (var (q, correct) in verdicts)
        {
            rec.Questions.Add(new AttemptQuestionRecord
            {
                Number = n++,
                QuestionId = q.Id,
                Prompt = q.Prompt,
                IsCorrect = correct,
            });
        }
        return rec;
    }

    private static DateTimeOffset Day(int d) => new(2026, 10, 1 + d, 9, 0, 0, TimeSpan.Zero);

    // ----- WrongAnswerSelector -------------------------------------------- //

    [Fact]
    public void RecentlyRightDropsOutRecentlyWrongStays()
    {
        var (doc, q) = QuizWith("P1", "P2");
        var attempts = new[]
        {
            AttemptOn(Day(0), (q[0], false), (q[1], false)),
            AttemptOn(Day(1), (q[0], true), (q[1], false)),   // q[0] fixed
        };

        var wrong = WrongAnswerSelector.SelectWrongQuestions(doc, attempts);

        Assert.Single(wrong);
        Assert.Equal(q[1].Id, wrong[0].Id);
    }

    [Fact]
    public void LatestVerdictWinsRegardlessOfListOrder()
    {
        var (doc, q) = QuizWith("P1");
        // Newest (right) first in the list, older (wrong) second.
        var attempts = new[]
        {
            AttemptOn(Day(5), (q[0], true)),
            AttemptOn(Day(1), (q[0], false)),
        };

        Assert.Empty(WrongAnswerSelector.SelectWrongQuestions(doc, attempts));
    }

    [Fact]
    public void NonGradeableMostRecentVerdictIsExcluded()
    {
        var (doc, q) = QuizWith("P1");
        var attempts = new[]
        {
            AttemptOn(Day(0), (q[0], false)),
            AttemptOn(Day(1), (q[0], (bool?)null)),   // essay / needs review latest
        };

        Assert.Empty(WrongAnswerSelector.SelectWrongQuestions(doc, attempts));
    }

    [Fact]
    public void LegacyRecordWithNoIdMatchesByPrompt()
    {
        var (doc, q) = QuizWith("Capital of France?");
        var rec = new AttemptRecord { TakenAt = Day(0) };
        rec.Questions.Add(new AttemptQuestionRecord
        {
            // QuestionId left default (Guid.Empty) -> must fall back to prompt.
            Prompt = "Capital of France?",
            IsCorrect = false,
        });

        var wrong = WrongAnswerSelector.SelectWrongQuestions(doc, new[] { rec });

        Assert.Single(wrong);
        Assert.Equal(q[0].Id, wrong[0].Id);
    }

    [Fact]
    public void IdMatchSurvivesAPromptEdit()
    {
        var (doc, q) = QuizWith("New prompt");
        // The record was written with the OLD prompt but the right id.
        var rec = new AttemptRecord { TakenAt = Day(0) };
        rec.Questions.Add(new AttemptQuestionRecord
        {
            QuestionId = q[0].Id,
            Prompt = "Old prompt",
            IsCorrect = false,
        });

        var wrong = WrongAnswerSelector.SelectWrongQuestions(doc, new[] { rec });

        Assert.Single(wrong);
        Assert.Equal(q[0].Id, wrong[0].Id);
    }

    [Fact]
    public void DeletedQuestionIsNotInTheSet()
    {
        var (doc, q) = QuizWith("P1");
        var rec = new AttemptRecord { TakenAt = Day(0) };
        rec.Questions.Add(new AttemptQuestionRecord
        {
            QuestionId = Guid.NewGuid(),   // not in the live quiz
            Prompt = "Deleted?",
            IsCorrect = false,
        });
        rec.Questions.Add(new AttemptQuestionRecord
        {
            QuestionId = q[0].Id,
            Prompt = "P1",
            IsCorrect = false,
        });

        var wrong = WrongAnswerSelector.SelectWrongQuestions(doc, new[] { rec });

        Assert.Single(wrong);
        Assert.Equal(q[0].Id, wrong[0].Id);
    }

    [Fact]
    public void EmptyHistoryGivesEmptySet()
    {
        var (doc, _) = QuizWith("P1");
        Assert.Empty(WrongAnswerSelector.SelectWrongQuestions(doc, Array.Empty<AttemptRecord>()));
    }

    [Fact]
    public void OutputPreservesQuizOrder()
    {
        var (doc, q) = QuizWith("P1", "P2", "P3");
        // An attempt that lists q3 before q1.
        var attempts = new[] { AttemptOn(Day(0), (q[2], false), (q[0], false)) };

        var wrong = WrongAnswerSelector.SelectWrongQuestions(doc, attempts);

        Assert.Equal(new[] { q[0].Id, q[2].Id }, wrong.Select(w => w.Id).ToArray());
    }

    // ----- QuizStatisticsService ------------------------------------------ //

    [Fact]
    public void StatsBasicAggregates()
    {
        var (_, q) = QuizWith("P1", "P2");
        var attempts = new List<AttemptRecord>
        {
            Graded(Day(0), 50, passed: false, seconds: 100, (q[0], true), (q[1], false)),
            Graded(Day(1), 100, passed: true, seconds: 200, (q[0], true), (q[1], true)),
        };

        var s = QuizStatisticsService.Compute(attempts);

        Assert.Equal(2, s.AttemptCount);
        Assert.Equal(2, s.GradedAttemptCount);
        Assert.Equal(75.0, s.AveragePercentage);
        Assert.Equal(100.0, s.BestPercentage);
        Assert.Equal(100.0, s.LatestPercentage);     // the Day(1) attempt
        Assert.Equal(1, s.PassCount);
        Assert.Equal(1, s.FailCount);
        Assert.Equal(150, s.AverageElapsedSeconds);
    }

    [Fact]
    public void EssaysNeverAppearInHardest()
    {
        var (_, q) = QuizWith("Gradeable", "Essay");
        var attempts = new List<AttemptRecord>
        {
            Graded(Day(0), 0, passed: false, seconds: 60, (q[0], false), (q[1], (bool?)null)),
            Graded(Day(1), 0, passed: false, seconds: 60, (q[0], false), (q[1], (bool?)null)),
        };

        var s = QuizStatisticsService.Compute(attempts);

        Assert.DoesNotContain(s.Hardest, h => h.Prompt == "Essay");
        Assert.Equal("Gradeable", s.Hardest[0].Prompt);
        Assert.Equal(2, s.Hardest[0].TimesWrong);
        Assert.Equal(2, s.Hardest[0].TimesGraded);
    }

    [Fact]
    public void AllEssayAttemptContributesNoPercentage()
    {
        var (_, q) = QuizWith("Essay");
        var rec = new AttemptRecord
        {
            TakenAt = Day(0),
            Percentage = null,
            Passed = null,
            ElapsedSeconds = 30,
        };
        rec.Questions.Add(new AttemptQuestionRecord { QuestionId = q[0].Id, Prompt = "Essay", IsCorrect = null });

        var s = QuizStatisticsService.Compute(new List<AttemptRecord> { rec });

        Assert.Equal(1, s.AttemptCount);
        Assert.Equal(0, s.GradedAttemptCount);
        Assert.Null(s.AveragePercentage);
        Assert.Null(s.LatestPercentage);
    }

    [Fact]
    public void HardestIsOrderedByMissRate()
    {
        var (_, q) = QuizWith("Easy", "Medium", "Hard");
        var attempts = new List<AttemptRecord>
        {
            Graded(Day(0), 33, passed: false, seconds: 90, (q[0], true), (q[1], false), (q[2], false)),
            Graded(Day(1), 66, passed: false, seconds: 90, (q[0], true), (q[1], true), (q[2], false)),
        };

        var s = QuizStatisticsService.Compute(attempts);

        Assert.Equal(new[] { "Hard", "Medium", "Easy" }, s.Hardest.Select(h => h.Prompt).ToArray());
    }

    [Fact]
    public void EmptyStatsIsEmpty()
    {
        var s = QuizStatisticsService.Compute(Array.Empty<AttemptRecord>());
        Assert.False(s.HasAnyAttempts);
        Assert.Equal(0, s.AttemptCount);
        Assert.Null(s.AveragePercentage);
        Assert.Empty(s.Hardest);
    }

    private static AttemptRecord Graded(
        DateTimeOffset at, double percentage, bool passed, int seconds,
        params (Question Question, bool? Correct)[] verdicts)
    {
        var rec = AttemptOn(at, verdicts);
        rec.Percentage = percentage;
        rec.Passed = passed;
        rec.ElapsedSeconds = seconds;
        return rec;
    }

    // ----- QuestionId persistence + legacy load --------------------------- //

    [Fact]
    public void QuestionIdRoundTripsThroughHistoryFile()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var qid = Guid.NewGuid();
            var quizId = Guid.NewGuid();

            var record = new AttemptRecord { QuizId = quizId, TakenAt = Day(0) };
            record.Questions.Add(new AttemptQuestionRecord
            {
                Number = 1, QuestionId = qid, Prompt = "P", IsCorrect = false,
            });

            var write = new AttemptHistoryService(dir);
            write.Load();
            write.Add(record);
            write.Save();

            var read = new AttemptHistoryService(dir);
            read.Load();
            var loaded = read.ForQuiz(quizId).Single().Questions.Single();

            Assert.Equal(qid, loaded.QuestionId);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void LegacyHistoryWithoutQuestionIdStillLoads()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var quizId = Guid.NewGuid();

            // A history.json shaped like an older build: camelCase keys (the
            // serializer's naming policy) and a question record with no
            // questionId field at all. It must load (QuestionId defaults to
            // Guid.Empty), not throw or drop the whole file.
            var json = $$"""
                [
                  {
                    "quizId": "{{quizId}}",
                    "quizTitle": "Legacy",
                    "takenAt": "2026-10-01T09:00:00+00:00",
                    "percentage": 50,
                    "passed": false,
                    "scoredPoints": 5,
                    "autoGradedPoints": 10,
                    "elapsedSeconds": 60,
                    "questions": [
                      { "number": 1, "prompt": "P1", "givenAnswer": "a", "correctAnswer": "b", "scored": 0, "possible": 1, "isCorrect": false, "needsReview": false }
                    ]
                  }
                ]
                """;
            File.WriteAllText(Path.Combine(dir, "history.json"), json);

            var svc = new AttemptHistoryService(dir);
            svc.Load();

            var loaded = svc.ForQuiz(quizId).Single().Questions.Single();
            Assert.Equal(Guid.Empty, loaded.QuestionId);   // defaulted, not crashed
            Assert.Equal("P1", loaded.Prompt);
            Assert.False(loaded.IsCorrect);
        }
        finally { Directory.Delete(dir, true); }
    }
}
