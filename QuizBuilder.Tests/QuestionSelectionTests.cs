using System;
using System.Collections.Generic;
using System.Linq;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Question selection modes: exact-count-per-section and the proportional
/// total-count distribution. The distribution is largest-remainder apportionment;
/// these pin down the properties that make it correct -- exact totals, never more
/// than a section holds, and proportional shares.
/// </summary>
public class QuestionSelectionTests
{
    private static QuizDocument WithSections(params int[] poolSizes)
    {
        var doc = new QuizDocument { Title = "T" };

        foreach (var (size, index) in poolSizes.Select((s, i) => (s, i)))
        {
            var section = new Section { Title = $"S{index}" };
            for (var q = 0; q < size; q++)
                section.Questions.Add(new TrueFalseQuestion { Prompt = $"S{index}Q{q}", Points = 1, CorrectAnswer = true });
            doc.Sections.Add(section);
            doc.SectionDisplayOrder.Add(section.Id);
        }

        return doc;
    }

    private static QuizSettings TotalCount(int total) => new()
    {
        SelectionMode = QuestionSelectionMode.TotalCount,
        TotalQuestionCount = total,
    };

    private static List<int> Counts(CompiledQuiz quiz) =>
        quiz.Sections.Select(s => s.Questions.Count).ToList();

    [Fact]
    public void TotalCountSplitsEvenlyAcrossEqualSections()
    {
        var doc = WithSections(10, 10, 10);

        var quiz = new QuizCompiler().Compile(doc, TotalCount(15), seed: 0);

        Assert.Equal(15, quiz.QuestionCount);
        Assert.Equal(new[] { 5, 5, 5 }, Counts(quiz));
    }

    [Fact]
    public void TotalCountIsProportionalToPoolSize()
    {
        var doc = WithSections(20, 10);   // 2:1

        var quiz = new QuizCompiler().Compile(doc, TotalCount(9), seed: 0);

        Assert.Equal(9, quiz.QuestionCount);
        Assert.Equal(new[] { 6, 3 }, Counts(quiz));
    }

    [Fact]
    public void TotalCountNeverTakesMoreThanASectionHolds()
    {
        // Section 0's proportional share of 10 is tiny (3/103), so it contributes
        // 0 -- and crucially never a number above its pool of 3.
        var doc = WithSections(3, 100);

        var quiz = new QuizCompiler().Compile(doc, TotalCount(10), seed: 0);

        Assert.Equal(10, quiz.QuestionCount);
        var counts = Counts(quiz);
        Assert.True(counts[0] <= 3);
        Assert.True(counts[1] <= 100);
    }

    [Fact]
    public void TotalCountAboveTheQuizSizeTakesEverything()
    {
        var doc = WithSections(4, 6);

        var quiz = new QuizCompiler().Compile(doc, TotalCount(50), seed: 0);

        Assert.Equal(10, quiz.QuestionCount);
        Assert.Equal(new[] { 4, 6 }, Counts(quiz));
    }

    [Fact]
    public void TotalCountOfZeroTakesNothing()
    {
        var doc = WithSections(5, 5);

        var quiz = new QuizCompiler().Compile(doc, TotalCount(0), seed: 0);

        Assert.Equal(0, quiz.QuestionCount);
    }

    [Fact]
    public void TotalCountRoundingStillSumsExactly()
    {
        var doc = WithSections(7, 7, 7);   // 10 * 7/21 = 3.33 each

        var quiz = new QuizCompiler().Compile(doc, TotalCount(10), seed: 0);

        Assert.Equal(10, quiz.QuestionCount);
        // One section gets the rounding leftover: 4, 3, 3 in some order.
        Assert.Equal(new[] { 3, 3, 4 }, Counts(quiz).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void TotalCountBiggerSectionNeverGetsFewerThanASmallerOne()
    {
        var doc = WithSections(15, 9, 6);

        var quiz = new QuizCompiler().Compile(doc, TotalCount(12), seed: 0);
        var counts = Counts(quiz);

        Assert.Equal(12, quiz.QuestionCount);
        Assert.True(counts[0] >= counts[1]);
        Assert.True(counts[1] >= counts[2]);
    }

    [Fact]
    public void ExactCountPerSectionTakesTheConfiguredNumber()
    {
        var doc = WithSections(10, 8);
        var sectionIds = doc.Sections.Select(s => s.Id).ToList();

        var settings = new QuizSettings { SelectionMode = QuestionSelectionMode.ExactCountPerSection };
        settings.QuestionCountPerSection[sectionIds[0].ToString()] = 4;
        settings.QuestionCountPerSection[sectionIds[1].ToString()] = 3;

        var quiz = new QuizCompiler().Compile(doc, settings, seed: 0);

        Assert.Equal(new[] { 4, 3 }, Counts(quiz));
    }

    [Fact]
    public void AllQuestionsModeTakesEverything()
    {
        var doc = WithSections(5, 7);

        var quiz = new QuizCompiler().Compile(doc, new QuizSettings(), seed: 0);

        Assert.Equal(12, quiz.QuestionCount);
    }
}
