using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// GrammarScopeBuilder: turns the inventory + a chosen scope into the engine's
/// GrammarFields, strips the description, drops empties, assigns stable ids, and
/// keeps the id→source back-map. C# port of tools/port/grammar_scope_port.py.
/// </summary>
public class GrammarScopeBuilderTests
{
    private static QuizDocument BuildDoc(out Guid secA, out Guid secB)
    {
        var doc = new QuizDocument { Title = "My Quiz", Description = "<strong>Intro</strong> to the <ul><li>topic</li></ul>" };

        var a = new Section { Title = "Section A" };
        a.Questions.Add(new MultipleChoiceSingleQuestion
        {
            Prompt = "Their going home.",
            Choices = { new Choice { Text = "A apple" } }
        });

        var b = new Section { Title = "Section B" };
        b.Questions.Add(new TrueFalseQuestion { Prompt = "Its raining." });

        doc.Sections.Add(a);
        doc.Sections.Add(b);
        doc.StudyCards.Add(new StudyCard { Front = "Term", Back = "Definiton" });

        secA = a.Id;
        secB = b.Id;
        return doc;
    }

    private static IReadOnlyList<TextField> Inventory(QuizDocument doc) =>
        DocumentTextInventory.Enumerate(doc);

    [Fact]
    public void WholeQuizScopeIncludesEverything()
    {
        var doc = BuildDoc(out _, out _);
        var sel = GrammarScopeBuilder.Build(Inventory(doc), GrammarScope.WholeQuiz);
        // title, description, 2 section titles, 2 prompts, 1 choice, 2 study card sides
        Assert.True(sel.Fields.Count >= 8);
        Assert.Contains(sel.Fields, f => f.Text.Contains("Their going"));
        Assert.Contains(sel.Fields, f => f.Text == "Term");
    }

    [Fact]
    public void StudyCardsScopeSelectsOnlyCards()
    {
        var doc = BuildDoc(out _, out _);
        var sel = GrammarScopeBuilder.Build(Inventory(doc), GrammarScope.StudyCards);
        Assert.Equal(2, sel.Fields.Count);
        Assert.Contains(sel.Fields, f => f.Text == "Term");
        Assert.Contains(sel.Fields, f => f.Text == "Definiton");
        Assert.DoesNotContain(sel.Fields, f => f.Text.Contains("Their going"));
    }

    [Fact]
    public void SectionScopeSelectsOnlyThatSection()
    {
        var doc = BuildDoc(out var secA, out _);
        var sel = GrammarScopeBuilder.Build(Inventory(doc), GrammarScope.Section, secA);
        // Section A: its title, its prompt, its choice
        Assert.Contains(sel.Fields, f => f.Text == "Section A");
        Assert.Contains(sel.Fields, f => f.Text == "Their going home.");
        Assert.Contains(sel.Fields, f => f.Text == "A apple");
        // nothing from Section B or study cards
        Assert.DoesNotContain(sel.Fields, f => f.Text == "Section B");
        Assert.DoesNotContain(sel.Fields, f => f.Text == "Its raining.");
        Assert.DoesNotContain(sel.Fields, f => f.Text == "Term");
    }

    [Fact]
    public void SectionScopeWithNullIdIsEmpty()
    {
        var doc = BuildDoc(out _, out _);
        var sel = GrammarScopeBuilder.Build(Inventory(doc), GrammarScope.Section, sectionId: null);
        Assert.False(sel.HasFields);
    }

    [Fact]
    public void DescriptionIsStrippedAndMarkedNonReplaceable()
    {
        var doc = BuildDoc(out _, out _);
        var sel = GrammarScopeBuilder.Build(Inventory(doc), GrammarScope.WholeQuiz);

        var desc = Assert.Single(sel.Fields, f => f.Label == "Quiz description");
        foreach (var tag in new[] { "strong", "ul", "li" })
            Assert.DoesNotContain(tag, desc.Text.Split());
        Assert.Contains("Intro", desc.Text);
        Assert.Contains("topic", desc.Text);
        Assert.False(sel.Replaceable[desc.FieldId]);

        var prompt = Assert.Single(sel.Fields, f => f.Text == "Their going home.");
        Assert.True(sel.Replaceable[prompt.FieldId]);
    }

    [Fact]
    public void IdsAreSequentialAndBackMapRoutesHome()
    {
        var doc = BuildDoc(out _, out _);
        var sel = GrammarScopeBuilder.Build(Inventory(doc), GrammarScope.StudyCards);

        Assert.Equal(Enumerable.Range(0, sel.Fields.Count), sel.Fields.Select(f => f.FieldId));

        var back = Assert.Single(sel.Fields, f => f.Text == "Definiton");
        // the back-map entry for that id resolves to a TextField whose text is the card back
        Assert.Equal("Definiton", sel.BackMap[back.FieldId].Text);
    }

    [Fact]
    public void EmptyFieldsAreDropped()
    {
        var doc = new QuizDocument { Title = "Only a title" }; // no description, no sections
        var sel = GrammarScopeBuilder.Build(Inventory(doc), GrammarScope.WholeQuiz);
        // Only the title has text; description is empty and dropped.
        Assert.Single(sel.Fields);
        Assert.Equal("Only a title", sel.Fields[0].Text);
    }
}
