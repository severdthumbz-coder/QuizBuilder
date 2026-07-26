using System;
using System.IO;
using System.Linq;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// The reusable question bank store. The invariants that matter: a stored
/// question is an independent copy (editing the source must not reach the bank),
/// bank questions are text-only, entries round-trip to disk with their
/// polymorphic type intact, and categories drive filtering.
/// </summary>
public class QuestionBankServiceTests : IDisposable
{
    private readonly string _dir;

    public QuestionBankServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "qb-bank-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static MultipleChoiceSingleQuestion Mc(string prompt = "Q")
    {
        var q = new MultipleChoiceSingleQuestion { Prompt = prompt, Points = 1 };
        q.Choices.Add(new Choice { Text = "a", IsCorrect = true });
        q.Choices.Add(new Choice { Text = "b" });
        return q;
    }

    [Fact]
    public void AddStoresACopyThatSurvivesReload()
    {
        var service = new QuestionBankService(_dir);
        service.Add(Mc("Capital of France?"), "Geography");

        var reloaded = new QuestionBankService(_dir);
        reloaded.Load();

        var entry = reloaded.All().Single();
        Assert.Equal("Geography", entry.Category);
        Assert.IsType<MultipleChoiceSingleQuestion>(entry.Question);
        Assert.Equal("Capital of France?", entry.Question.Prompt);
    }

    [Fact]
    public void TheStoredQuestionIsIndependentOfTheSource()
    {
        var source = Mc("original");

        var service = new QuestionBankService(_dir);
        var entry = service.Add(source, null);

        // Mutating the source afterwards must not change what the bank holds.
        source.Prompt = "changed";

        Assert.Equal("original", entry.Question.Prompt);
        // And the stored copy has its own id.
        Assert.NotEqual(source.Id, entry.Question.Id);
    }

    [Fact]
    public void BankQuestionsAreTextOnly()
    {
        var withImage = Mc();
        withImage.ImageRelativePath = "images/abc123.png";

        var service = new QuestionBankService(_dir);
        var entry = service.Add(withImage, null);

        // The image reference is dropped: the bank has no package to resolve it.
        Assert.Null(entry.Question.ImageRelativePath);
    }

    [Fact]
    public void PolymorphicTypesRoundTrip()
    {
        var service = new QuestionBankService(_dir);
        service.Add(Mc(), "A");
        service.Add(new TrueFalseQuestion { Prompt = "T?", Points = 1, CorrectAnswer = true }, "B");

        var essay = new EssayQuestion { Prompt = "Discuss", Points = 5 };
        service.Add(essay, "C");

        var reloaded = new QuestionBankService(_dir);
        reloaded.Load();

        var types = reloaded.All().Select(e => e.Question.GetType()).ToList();
        Assert.Contains(typeof(MultipleChoiceSingleQuestion), types);
        Assert.Contains(typeof(TrueFalseQuestion), types);
        Assert.Contains(typeof(EssayQuestion), types);
    }

    [Fact]
    public void CategoriesAreDistinctSortedAndBlankFree()
    {
        var service = new QuestionBankService(_dir);
        service.Add(Mc(), "Geography");
        service.Add(Mc(), "algebra");
        service.Add(Mc(), "Geography");   // duplicate
        service.Add(Mc(), "   ");         // blank -> ignored
        service.Add(Mc(), null);          // none -> ignored

        var categories = service.Categories();

        Assert.Equal(new[] { "algebra", "Geography" }, categories); // sorted, case-insensitive, deduped
    }

    [Fact]
    public void SetCategoryUpdatesInPlace()
    {
        var service = new QuestionBankService(_dir);
        var entry = service.Add(Mc(), "old");

        service.SetCategory(entry.Id, "new");

        Assert.Equal("new", service.All().Single().Category);
    }

    [Fact]
    public void SetCategoryTrimsAndNullsBlanks()
    {
        var service = new QuestionBankService(_dir);
        var entry = service.Add(Mc(), "x");

        service.SetCategory(entry.Id, "   ");

        Assert.Null(service.All().Single().Category);
    }

    [Fact]
    public void RemoveDeletesFromDiskToo()
    {
        var service = new QuestionBankService(_dir);
        var entry = service.Add(Mc(), null);
        service.Remove(entry.Id);

        Assert.Empty(service.All());

        var reloaded = new QuestionBankService(_dir);
        reloaded.Load();
        Assert.Empty(reloaded.All());
    }

    [Fact]
    public void AllIsNewestFirst()
    {
        var service = new QuestionBankService(_dir);
        var first = service.Add(Mc("first"), null);
        System.Threading.Thread.Sleep(5);
        var second = service.Add(Mc("second"), null);

        var all = service.All();
        Assert.Equal(second.Id, all[0].Id);
        Assert.Equal(first.Id, all[1].Id);
    }

    [Fact]
    public void LoadWithNoFileIsEmpty()
    {
        var service = new QuestionBankService(_dir);
        service.Load();
        Assert.Empty(service.All());
    }
}
