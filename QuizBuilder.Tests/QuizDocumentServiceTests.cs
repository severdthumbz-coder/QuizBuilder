using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Move semantics are the easiest thing here to get subtly wrong -- an
/// off-by-one only shows up when a user drags a question below the last item
/// and the app throws. These cases were validated against a reference model
/// before the implementation was written; they are pinned here so a future
/// refactor cannot quietly reintroduce the bug.
/// </summary>
public class QuizDocumentServiceTests
{
    private static (QuizDocumentService svc, Section section, Question[] questions) MakeSection(
        params string[] prompts)
    {
        var svc = new QuizDocumentService();
        var section = svc.AddSection("S1");
        var questions = prompts
            .Select(p => (Question)new TrueFalseQuestion { Prompt = p })
            .ToArray();

        foreach (var q in questions)
            svc.AddQuestion(section.Id, q);

        return (svc, section, questions);
    }

    private static string[] Prompts(Section s) => s.Questions.Select(q => q.Prompt).ToArray();

    [Fact]
    public void MoveQuestion_ToEnd_WhenDroppedPastLastItem()
    {
        var (svc, section, q) = MakeSection("A", "B", "C");

        // Index 3 in a 3-item list: what a drag below the last row produces.
        svc.MoveQuestion(section.Id, q[0].Id, section.Id, 3);

        Assert.Equal(new[] { "B", "C", "A" }, Prompts(section));
    }

    [Fact]
    public void MoveQuestion_ClampsWildIndex_WithoutThrowing()
    {
        var (svc, section, q) = MakeSection("A", "B", "C");

        svc.MoveQuestion(section.Id, q[0].Id, section.Id, 99);

        Assert.Equal(new[] { "B", "C", "A" }, Prompts(section));
    }

    [Fact]
    public void MoveQuestion_ToFront()
    {
        var (svc, section, q) = MakeSection("A", "B", "C");

        svc.MoveQuestion(section.Id, q[2].Id, section.Id, 0);

        Assert.Equal(new[] { "C", "A", "B" }, Prompts(section));
    }

    [Fact]
    public void MoveQuestion_DownOnePosition()
    {
        var (svc, section, q) = MakeSection("A", "B", "C");

        svc.MoveQuestion(section.Id, q[0].Id, section.Id, 1);

        Assert.Equal(new[] { "B", "A", "C" }, Prompts(section));
    }

    [Fact]
    public void MoveQuestion_AcrossSections_InsertsAtRequestedIndex()
    {
        var svc = new QuizDocumentService();
        var s1 = svc.AddSection("S1");
        var s2 = svc.AddSection("S2");

        var a = new TrueFalseQuestion { Prompt = "A" };
        svc.AddQuestion(s1.Id, a);
        svc.AddQuestion(s1.Id, new TrueFalseQuestion { Prompt = "B" });
        svc.AddQuestion(s2.Id, new TrueFalseQuestion { Prompt = "X" });
        svc.AddQuestion(s2.Id, new TrueFalseQuestion { Prompt = "Y" });

        svc.MoveQuestion(s1.Id, a.Id, s2.Id, 0);

        Assert.Equal(new[] { "B" }, Prompts(s1));
        Assert.Equal(new[] { "A", "X", "Y" }, Prompts(s2));
    }

    [Fact]
    public void MoveQuestion_AcrossSections_ToEmptySection()
    {
        var svc = new QuizDocumentService();
        var s1 = svc.AddSection("S1");
        var s2 = svc.AddSection("S2");

        var a = new TrueFalseQuestion { Prompt = "A" };
        svc.AddQuestion(s1.Id, a);

        svc.MoveQuestion(s1.Id, a.Id, s2.Id, 0);

        Assert.Empty(s1.Questions);
        Assert.Equal(new[] { "A" }, Prompts(s2));
    }

    [Fact]
    public void SectionsInDisplayOrder_FallsBackForUnlistedSections()
    {
        var svc = new QuizDocumentService();
        var s1 = svc.AddSection("First");
        var s2 = svc.AddSection("Second");
        var s3 = svc.AddSection("Third");

        // Only s3 is explicitly ordered; the rest must still appear.
        svc.SetSectionDisplayOrder(new[] { s3.Id });

        var ordered = svc.Current.SectionsInDisplayOrder().Select(s => s.Title).ToArray();

        Assert.Equal(new[] { "Third", "First", "Second" }, ordered);
    }

    [Fact]
    public void SectionsInDisplayOrder_IgnoresStaleIds()
    {
        var svc = new QuizDocumentService();
        var s1 = svc.AddSection("First");

        // A section id that no longer exists must not break enumeration.
        svc.SetSectionDisplayOrder(new[] { Guid.NewGuid(), s1.Id });

        var ordered = svc.Current.SectionsInDisplayOrder().Select(s => s.Title).ToArray();

        Assert.Equal(new[] { "First" }, ordered);
    }

    [Fact]
    public void RemoveSection_AlsoRemovesFromDisplayOrder()
    {
        var svc = new QuizDocumentService();
        var s1 = svc.AddSection("First");

        svc.RemoveSection(s1.Id);

        Assert.Empty(svc.Current.Sections);
        Assert.DoesNotContain(s1.Id, svc.Current.SectionDisplayOrder);
    }

    [Fact]
    public void NewDocument_ResetsDirtyFlag()
    {
        var svc = new QuizDocumentService();
        svc.AddSection("Dirty me");
        Assert.True(svc.IsDirty);

        svc.NewDocument();

        Assert.False(svc.IsDirty);
    }

    [Fact]
    public void Mutation_RaisesDocumentChangedWithCorrectKind()
    {
        var svc = new QuizDocumentService();
        DocumentChangeKind? observed = null;
        svc.DocumentChanged += (_, e) => observed = e.Kind;

        svc.AddSection("S");

        Assert.Equal(DocumentChangeKind.SectionAdded, observed);
    }

    [Fact]
    public void MoveSection_KeepsDisplayOrderInStepWithTheList()
    {
        var svc = new QuizDocumentService();
        var a = svc.AddSection("Intro");
        var b = svc.AddSection("Middle");
        var c = svc.AddSection("End");

        svc.MoveSection(c.Id, 0);

        // The list the UI binds to, and the order used for publishing, must
        // agree. SectionDisplayOrder wins in SectionsInDisplayOrder(), so a
        // stale one would silently export the pre-drag order.
        Assert.Equal(
            svc.Current.Sections.Select(s => s.Id),
            svc.Current.SectionsInDisplayOrder().Select(s => s.Id));

        Assert.Equal(new[] { c.Id, a.Id, b.Id }, svc.Current.SectionDisplayOrder);
    }

    [Fact]
    public void MoveSection_ThenRemove_LeavesNoStaleIds()
    {
        var svc = new QuizDocumentService();
        var a = svc.AddSection("A");
        var b = svc.AddSection("B");

        svc.MoveSection(b.Id, 0);
        svc.RemoveSection(b.Id);

        Assert.DoesNotContain(b.Id, svc.Current.SectionDisplayOrder);
        Assert.Equal(new[] { a.Id }, svc.Current.SectionsInDisplayOrder().Select(s => s.Id));
    }

    [Fact]
    public void Description_SurvivesSetAndRead()
    {
        var svc = new QuizDocumentService();

        svc.SetDescription("Closed book. 90 minutes.");

        Assert.Equal("Closed book. 90 minutes.", svc.Current.Description);
        Assert.True(svc.IsDirty);
    }

    [Fact]
    public void Description_DefaultsToEmptyForDocumentsThatPredateTheField()
    {
        // System.Text.Json leaves a property at its field initialiser when the
        // key is absent, so an old .qbx (written before Description existed)
        // loads as "" rather than null. A non-nullable string holding null
        // would throw on the first .Trim() somewhere far from here.
        var json = """{"title":"Old Quiz","sections":[]}""";

        var doc = System.Text.Json.JsonSerializer.Deserialize<QuizDocument>(
            json, SettingsService.JsonOptions);

        Assert.NotNull(doc);
        Assert.NotNull(doc!.Description);
        Assert.Equal(string.Empty, doc.Description);
    }

    [Fact]
    public void LoadedDocument_CarriesItsQuestions_SoTheTabHasSomethingToShow()
    {
        // The Quiz Builder tab showed an empty question list after Open. The
        // cause was in the ViewModel's selection logic, not here -- but this
        // pins the contract that fix relies on: a round-tripped section really
        // does still hold its questions, with their concrete types intact.
        var svc = new QuizDocumentService();
        var section = svc.AddSection("Chapter 1");
        svc.AddQuestion(section.Id, new MultipleChoiceSingleQuestion { Prompt = "Q1" });
        svc.AddQuestion(section.Id, new EssayQuestion { Prompt = "Q2" });

        var json = System.Text.Json.JsonSerializer.Serialize(svc.Current, SettingsService.JsonOptions);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<QuizDocument>(json, SettingsService.JsonOptions);

        Assert.NotNull(loaded);
        var loadedSection = Assert.Single(loaded!.Sections);
        Assert.Equal(2, loadedSection.Questions.Count);
        Assert.IsType<MultipleChoiceSingleQuestion>(loadedSection.Questions[0]);
        Assert.IsType<EssayQuestion>(loadedSection.Questions[1]);
    }

    [Fact]
    public void LoadDocument_RaisesDocumentReplaced_WhichIsTheTabsCueToRebuild()
    {
        var svc = new QuizDocumentService();
        var kinds = new List<DocumentChangeKind>();

        var doc = new QuizDocument { Title = "Loaded" };
        doc.Sections.Add(new Section { Title = "S" });

        svc.DocumentChanged += (_, e) => kinds.Add(e.Kind);
        svc.LoadDocument(doc, "C:\\some\\path.qbx");

        // OpenAsync relies on this single event to trigger the rebuild rather
        // than calling Rebuild() itself, so it must fire exactly once.
        Assert.Equal(new[] { DocumentChangeKind.DocumentReplaced }, kinds);
    }
}
