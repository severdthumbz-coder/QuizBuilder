using System;
using System.Collections.Generic;
using System.Linq;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Choosing sections at quiz time: the compiler's runtime section filter. The
/// filter must apply only under SelectAtQuizTime, and a stale set under any other
/// scope must be ignored so sections are never silently dropped.
/// </summary>
public class RuntimeSectionSelectionTests
{
    private static (QuizDocument doc, Guid a, Guid b, Guid c) ThreeSections()
    {
        var doc = new QuizDocument { Title = "T" };

        Section Make(string title)
        {
            var s = new Section { Title = title };
            var q = new TrueFalseQuestion { Prompt = $"{title} q", Points = 1, CorrectAnswer = true };
            s.Questions.Add(q);
            doc.Sections.Add(s);
            doc.SectionDisplayOrder.Add(s.Id);
            return s;
        }

        var a = Make("A");
        var b = Make("B");
        var c = Make("C");
        return (doc, a.Id, b.Id, c.Id);
    }

    private static QuizSettings Settings(GradingScope scope) => new() { GradingScope = scope };

    [Fact]
    public void AllSectionsScopeIncludesEverything()
    {
        var (doc, _, _, _) = ThreeSections();

        var quiz = new QuizCompiler().Compile(doc, Settings(GradingScope.AllSections), seed: 0);

        Assert.Equal(3, quiz.Sections.Count);
    }

    [Fact]
    public void AllSectionsScopeIgnoresAStaleSelectionSet()
    {
        var (doc, a, _, _) = ThreeSections();

        // A selection set is passed, but the scope is AllSections: it must be
        // ignored, not silently drop B and C.
        var quiz = new QuizCompiler().Compile(
            doc, Settings(GradingScope.AllSections), seed: 0, new HashSet<Guid> { a });

        Assert.Equal(3, quiz.Sections.Count);
    }

    [Fact]
    public void SelectAtQuizTimeIncludesOnlyChosenSections()
    {
        var (doc, a, _, c) = ThreeSections();

        var quiz = new QuizCompiler().Compile(
            doc, Settings(GradingScope.SelectAtQuizTime), seed: 0, new HashSet<Guid> { a, c });

        Assert.Equal(2, quiz.Sections.Count);
        Assert.Equal(new[] { "A", "C" }, quiz.Sections.Select(s => s.Title));
    }

    [Fact]
    public void SelectAtQuizTimePreservesDisplayOrderRegardlessOfSetOrder()
    {
        var (doc, a, _, c) = ThreeSections();

        // Set lists c before a; output must still be display order A, C.
        var quiz = new QuizCompiler().Compile(
            doc, Settings(GradingScope.SelectAtQuizTime), seed: 0, new HashSet<Guid> { c, a });

        Assert.Equal(new[] { "A", "C" }, quiz.Sections.Select(s => s.Title));
    }

    [Fact]
    public void SelectAtQuizTimeWithNullSetIncludesAll()
    {
        var (doc, _, _, _) = ThreeSections();

        // No choice supplied yet (null): behave as all sections.
        var quiz = new QuizCompiler().Compile(doc, Settings(GradingScope.SelectAtQuizTime), seed: 0, null);

        Assert.Equal(3, quiz.Sections.Count);
    }

    [Fact]
    public void SelectAtQuizTimeWithEmptySetIncludesNothing()
    {
        var (doc, _, _, _) = ThreeSections();

        var quiz = new QuizCompiler().Compile(
            doc, Settings(GradingScope.SelectAtQuizTime), seed: 0, new HashSet<Guid>());

        Assert.Empty(quiz.Sections);
    }
}
