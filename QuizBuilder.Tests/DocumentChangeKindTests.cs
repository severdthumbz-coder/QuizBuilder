using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// The Quiz Builder tab reacts to DocumentChanged BY KIND: three kinds are
/// raised by its own setters, and rebuilding the lists in response to those
/// destroys the control the user is typing into.
///
/// These tests pin the kind each operation raises. If someone later changes
/// RenameSection to raise SectionsReordered, say, the tab would start
/// rebuilding on every keystroke again and editing would silently break. That
/// is not a crash and no static check sees it, so it gets a test.
/// </summary>
public class DocumentChangeKindTests
{
    private static (QuizDocumentService svc, List<DocumentChangeKind> kinds) Track()
    {
        var svc = new QuizDocumentService();
        var kinds = new List<DocumentChangeKind>();
        svc.DocumentChanged += (_, e) => kinds.Add(e.Kind);
        return (svc, kinds);
    }

    [Fact]
    public void SetTitle_RaisesTitleChanged_Only()
    {
        var (svc, kinds) = Track();

        svc.SetTitle("My Quiz");

        Assert.Equal(new[] { DocumentChangeKind.TitleChanged }, kinds);
    }

    [Fact]
    public void SetTitle_WithNoRealChange_RaisesNothing()
    {
        var (svc, kinds) = Track();
        svc.SetTitle("Same");
        kinds.Clear();

        svc.SetTitle("Same");

        // Re-raising for a no-op change would put the caret trap back.
        Assert.Empty(kinds);
    }

    [Fact]
    public void RenameSection_RaisesSectionRenamed_NotReordered()
    {
        var (svc, kinds) = Track();
        var section = svc.AddSection("Intro");
        kinds.Clear();

        svc.RenameSection(section.Id, "Introduction");

        Assert.Equal(new[] { DocumentChangeKind.SectionRenamed }, kinds);
    }

    [Fact]
    public void RenameSection_CoercesBlankToUntitled()
    {
        var svc = new QuizDocumentService();
        var section = svc.AddSection("Intro");

        svc.RenameSection(section.Id, "   ");

        // The UI binds this with LostFocus precisely because of this rule:
        // with PropertyChanged, clearing the box to retype would reload
        // "Untitled Section" under the caret.
        Assert.Equal("Untitled Section", section.Title);
    }

    [Fact]
    public void NotifyQuestionChanged_RaisesQuestionChanged_Only()
    {
        var (svc, kinds) = Track();
        var section = svc.AddSection("S");
        var question = new Core.Models.EssayQuestion();
        svc.AddQuestion(section.Id, question);
        kinds.Clear();

        svc.NotifyQuestionChanged(section.Id, question.Id);

        // The editors raise this on every keystroke. The tab must be able to
        // ignore it, so it must not be conflated with QuestionAdded.
        Assert.Equal(new[] { DocumentChangeKind.QuestionChanged }, kinds);
    }

    [Fact]
    public void AddQuestion_RaisesQuestionAdded_SoTheListRebuilds()
    {
        var (svc, kinds) = Track();
        var section = svc.AddSection("S");
        kinds.Clear();

        svc.AddQuestion(section.Id, new Core.Models.EssayQuestion());

        Assert.Equal(new[] { DocumentChangeKind.QuestionAdded }, kinds);
    }

    [Fact]
    public void NewDocument_RaisesDocumentReplaced()
    {
        var (svc, kinds) = Track();
        svc.AddSection("S");
        kinds.Clear();

        svc.NewDocument();

        Assert.Contains(DocumentChangeKind.DocumentReplaced, kinds);
    }

    [Fact]
    public void EditingAQuestion_NeverRaisesAStructuralKind()
    {
        var (svc, kinds) = Track();
        var section = svc.AddSection("S");
        var question = new Core.Models.EssayQuestion();
        svc.AddQuestion(section.Id, question);
        kinds.Clear();

        // Simulate 20 keystrokes through the editor's path.
        for (var i = 0; i < 20; i++)
            svc.NotifyQuestionChanged(section.Id, question.Id);

        Assert.All(kinds, k => Assert.Equal(DocumentChangeKind.QuestionChanged, k));
        Assert.DoesNotContain(DocumentChangeKind.QuestionAdded, kinds);
        Assert.DoesNotContain(DocumentChangeKind.QuestionsReordered, kinds);
        Assert.DoesNotContain(DocumentChangeKind.DocumentReplaced, kinds);
    }

    [Fact]
    public void SetDescription_RaisesTitleChanged()
    {
        var (svc, kinds) = Track();

        svc.SetDescription("Answer all questions.");

        // Reuses TitleChanged rather than introducing a kind that every
        // existing switch would silently ignore.
        Assert.Equal(new[] { DocumentChangeKind.TitleChanged }, kinds);
    }

    [Fact]
    public void SetDescription_WithNoRealChange_RaisesNothing()
    {
        var (svc, kinds) = Track();
        svc.SetDescription("Same");
        kinds.Clear();

        svc.SetDescription("Same");

        Assert.Empty(kinds);
    }

    [Fact]
    public void SetDescription_KeepsBlankAsBlank()
    {
        var svc = new QuizDocumentService();
        svc.SetDescription("Something");

        svc.SetDescription("");

        // Unlike a section name, an empty description is a legitimate choice
        // and must not be coerced -- coercion is what makes a PropertyChanged
        // binding fight the caret.
        Assert.Equal(string.Empty, svc.Current.Description);
    }

    [Fact]
    public void NewDocument_HasAnEmptyDescription_NotNull()
    {
        var svc = new QuizDocumentService();

        Assert.Equal(string.Empty, svc.Current.Description);
    }
}
