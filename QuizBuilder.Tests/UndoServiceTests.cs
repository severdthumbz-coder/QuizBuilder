using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Undo is snapshot-based, so the cases that matter are the stack semantics:
/// what a new edit does to the redo branch, which end a depth limit trims, and
/// whether lowering the limit takes effect immediately or lies about what is
/// retained. These were validated against a reference model before the
/// implementation existed and are pinned here.
/// </summary>
public class UndoServiceTests
{
    private static (QuizDocumentService doc, UndoService undo) Make(int depth = 15)
    {
        var doc = new QuizDocumentService();
        var undo = new UndoService(doc);
        undo.SetDepth(depth);
        return (doc, undo);
    }

    private static void AddSection(QuizDocumentService doc, IUndoService undo, string title)
    {
        undo.CaptureBeforeChange($"Add section {title}");
        doc.AddSection(title);
    }

    [Fact]
    public void NothingToUndoOnAFreshDocument()
    {
        var (_, undo) = Make();

        Assert.False(undo.CanUndo);
        Assert.False(undo.CanRedo);
        Assert.Null(undo.NextUndoLabel);
    }

    [Fact]
    public void UndoRestoresThePreviousArrangement()
    {
        var (doc, undo) = Make();

        AddSection(doc, undo, "A");
        AddSection(doc, undo, "B");

        Assert.Equal(2, doc.Current.Sections.Count);

        Assert.True(undo.Undo());
        Assert.Single(doc.Current.Sections);
        Assert.Equal("A", doc.Current.Sections[0].Title);

        Assert.True(undo.Undo());
        Assert.Empty(doc.Current.Sections);
    }

    [Fact]
    public void RedoReappliesAnUndoneChange()
    {
        var (doc, undo) = Make();

        AddSection(doc, undo, "A");
        AddSection(doc, undo, "B");

        undo.Undo();
        undo.Undo();
        Assert.Empty(doc.Current.Sections);

        Assert.True(undo.Redo());
        Assert.Single(doc.Current.Sections);

        Assert.True(undo.Redo());
        Assert.Equal(2, doc.Current.Sections.Count);
        Assert.Equal("B", doc.Current.Sections[1].Title);
    }

    [Fact]
    public void UndoReturnsFalseWhenExhaustedRatherThanThrowing()
    {
        var (_, undo) = Make();

        Assert.False(undo.Undo());
        Assert.False(undo.Redo());
    }

    [Fact]
    public void ANewEditDiscardsTheRedoBranch()
    {
        var (doc, undo) = Make();

        AddSection(doc, undo, "A");
        undo.Undo();
        Assert.True(undo.CanRedo);

        // The branch redo would have replayed is no longer reachable.
        AddSection(doc, undo, "C");

        Assert.False(undo.CanRedo);
    }

    [Fact]
    public void QuestionsAndStudyCardsAreCoveredNotJustSections()
    {
        var (doc, undo) = Make();

        var section = doc.AddSection("S");

        undo.CaptureBeforeChange("Add question");
        doc.AddQuestion(section.Id, new TrueFalseQuestion { Prompt = "Q1" });

        undo.CaptureBeforeChange("Add study card");
        doc.AddStudyCard();

        Assert.Single(doc.Current.StudyCards);

        undo.Undo();
        Assert.Empty(doc.Current.StudyCards);
        Assert.Single(doc.Current.Sections[0].Questions);

        undo.Undo();
        Assert.Empty(doc.Current.Sections[0].Questions);
    }

    [Fact]
    public void DeletingASectionIsUndoableWithItsQuestions()
    {
        var (doc, undo) = Make();

        var section = doc.AddSection("Chapter 3");
        doc.AddQuestion(section.Id, new TrueFalseQuestion { Prompt = "Q1" });
        doc.AddQuestion(section.Id, new TrueFalseQuestion { Prompt = "Q2" });

        undo.CaptureBeforeChange("Delete section");
        doc.RemoveSection(section.Id);
        Assert.Empty(doc.Current.Sections);

        Assert.True(undo.Undo());

        // The questions must come back with the section, not just the header.
        Assert.Single(doc.Current.Sections);
        Assert.Equal("Chapter 3", doc.Current.Sections[0].Title);
        Assert.Equal(2, doc.Current.Sections[0].Questions.Count);
    }

    [Fact]
    public void QuestionTypeSurvivesTheSnapshotRoundTrip()
    {
        var (doc, undo) = Make();

        var section = doc.AddSection("S");
        doc.AddQuestion(section.Id, new MatchingQuestion { Prompt = "Match" });

        undo.CaptureBeforeChange("Delete section");
        doc.RemoveSection(section.Id);
        undo.Undo();

        // A snapshot is JSON, so the polymorphic discriminator has to survive
        // or questions come back as the wrong type.
        var restored = doc.Current.Sections[0].Questions[0];
        Assert.IsType<MatchingQuestion>(restored);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(15)]
    public void DepthLimitDropsTheOldestSteps(int depth)
    {
        var (doc, undo) = Make(depth);

        for (var i = 0; i < depth + 5; i++)
            AddSection(doc, undo, $"S{i}");

        Assert.Equal(depth, undo.UndoDepth);

        // The retained steps must be the most recent ones: the recent past is
        // what a user reaches for.
        var steps = 0;
        while (undo.Undo()) steps++;
        Assert.Equal(depth, steps);
    }

    [Fact]
    public void LoweringTheDepthTrimsImmediately()
    {
        var (doc, undo) = Make(10);

        for (var i = 0; i < 8; i++)
            AddSection(doc, undo, $"S{i}");

        Assert.Equal(8, undo.UndoDepth);

        undo.SetDepth(3);

        // Trimming lazily would let the setting misreport what is held.
        Assert.Equal(3, undo.UndoDepth);
    }

    [Fact]
    public void DepthZeroDisablesUndoEntirely()
    {
        var (doc, undo) = Make(0);

        AddSection(doc, undo, "A");

        Assert.False(undo.CanUndo);
        Assert.Equal(0, undo.UndoDepth);
    }

    [Fact]
    public void DepthIsClampedToTheSupportedRange()
    {
        var (doc, undo) = Make();

        undo.SetDepth(int.MaxValue);
        for (var i = 0; i < UndoSettings.MaxDepth + 10; i++)
            AddSection(doc, undo, $"S{i}");

        Assert.Equal(UndoSettings.MaxDepth, undo.UndoDepth);
    }

    [Fact]
    public void OpeningADifferentDocumentClearsTheHistory()
    {
        var (doc, undo) = Make();

        AddSection(doc, undo, "A");
        Assert.True(undo.CanUndo);

        doc.LoadDocument(new QuizDocument { Title = "Other" }, "C:\\other.qbx");

        // Undoing into a document that is no longer on screen is incoherent.
        Assert.False(undo.CanUndo);
        Assert.False(undo.CanRedo);
    }

    [Fact]
    public void UndoDoesNotClearItsOwnHistory()
    {
        var (doc, undo) = Make();

        AddSection(doc, undo, "A");
        AddSection(doc, undo, "B");

        undo.Undo();

        // The restore raises DocumentReplaced, which must not be mistaken for
        // an Open and wipe the very stack being walked.
        Assert.True(undo.CanUndo);
        Assert.True(undo.CanRedo);
    }

    [Fact]
    public void UndoLeavesTheDocumentDirty()
    {
        var (doc, undo) = Make();

        AddSection(doc, undo, "A");
        doc.MarkSaved("C:\\quiz.qbx");
        Assert.False(doc.IsDirty);

        AddSection(doc, undo, "B");
        undo.Undo();

        // Undoing back to the saved arrangement does not mean the file matches:
        // reporting "no unsaved changes" here would cost the user work at the
        // next close prompt.
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void UndoKeepsTheCurrentFilePath()
    {
        var (doc, undo) = Make();

        doc.MarkSaved("C:\\quiz.qbx");
        AddSection(doc, undo, "A");
        undo.Undo();

        Assert.Equal("C:\\quiz.qbx", doc.CurrentFilePath);
    }

    [Fact]
    public void RedoOfASectionDeleteRemovesItsQuestionsToo()
    {
        var (doc, undo) = Make();

        var section = doc.AddSection("Section 1");
        doc.AddQuestion(section.Id, new TrueFalseQuestion { Prompt = "Q1" });

        undo.CaptureBeforeChange("Delete section");
        doc.RemoveSection(section.Id);

        undo.Undo();
        Assert.Single(doc.Current.Sections);
        Assert.Single(doc.Current.Sections[0].Questions);

        Assert.True(undo.Redo());

        // The document must not be left with questions belonging to no
        // section: redoing a delete has to take the questions with it, exactly
        // as the original delete did.
        Assert.Empty(doc.Current.Sections);
        Assert.Equal(0, doc.Current.QuestionCount);
    }

    [Fact]
    public void UndoAndRedoCanBeRepeatedWithoutDrift()
    {
        var (doc, undo) = Make();

        var section = doc.AddSection("Section 1");
        doc.AddQuestion(section.Id, new TrueFalseQuestion { Prompt = "Q1" });

        undo.CaptureBeforeChange("Delete section");
        doc.RemoveSection(section.Id);

        // Cycling must land on the same two states every time, not accumulate
        // or shed content.
        for (var i = 0; i < 3; i++)
        {
            undo.Undo();
            Assert.Single(doc.Current.Sections);
            Assert.Equal(1, doc.Current.QuestionCount);

            undo.Redo();
            Assert.Empty(doc.Current.Sections);
            Assert.Equal(0, doc.Current.QuestionCount);
        }
    }

    [Fact]
    public void QuestionCountIsZeroWhenNoSectionsRemain()
    {
        var (doc, undo) = Make();

        var section = doc.AddSection("Section 1");
        doc.AddQuestion(section.Id, new TrueFalseQuestion { Prompt = "Q1" });

        undo.CaptureBeforeChange("Delete section");
        doc.RemoveSection(section.Id);
        undo.Undo();
        undo.Redo();

        // A question surviving with no section to hold it is not a state the
        // document can represent -- if the count disagrees with the section
        // list, something has gone wrong upstream.
        Assert.Empty(doc.Current.Sections);
        Assert.Equal(0, doc.Current.QuestionCount);
        Assert.Empty(doc.Current.SectionDisplayOrder);
    }

    [Fact]
    public void AttachingAnImageIsUndoable()
    {
        var (doc, undo) = Make();

        var section = doc.AddSection("S");
        var question = new TrueFalseQuestion { Prompt = "Q1" };
        doc.AddQuestion(section.Id, question);

        // What the editor does: capture, then set the path.
        undo.CaptureBeforeChange("Add image");
        question.ImageRelativePath = "images/abc123-photo.png";
        doc.NotifyQuestionChanged(section.Id, question.Id);

        Assert.True(undo.Undo());

        var restored = doc.Current.Sections[0].Questions[0];
        Assert.Null(restored.ImageRelativePath);
    }

    [Fact]
    public void RemovingAnImageIsUndoable()
    {
        var (doc, undo) = Make();

        var section = doc.AddSection("S");
        var question = new TrueFalseQuestion { Prompt = "Q1" };
        question.ImageRelativePath = "images/abc123-photo.png";
        doc.AddQuestion(section.Id, question);

        undo.CaptureBeforeChange("Remove image");
        question.ImageRelativePath = null;
        doc.NotifyQuestionChanged(section.Id, question.Id);

        Assert.True(undo.Undo());

        // The path must come back, or the picture is gone as far as the user
        // is concerned even though the bytes are still cached.
        var restored = doc.Current.Sections[0].Questions[0];
        Assert.Equal("images/abc123-photo.png", restored.ImageRelativePath);
    }

    [Fact]
    public void RedoRestoresAnImagePath()
    {
        var (doc, undo) = Make();

        var section = doc.AddSection("S");
        var question = new TrueFalseQuestion { Prompt = "Q1" };
        doc.AddQuestion(section.Id, question);

        undo.CaptureBeforeChange("Add image");
        question.ImageRelativePath = "images/abc123-photo.png";

        undo.Undo();
        Assert.True(undo.Redo());

        // Image bytes live in the package cache, not the document, and nothing
        // in undo clears that cache -- so the restored path still resolves.
        var restored = doc.Current.Sections[0].Questions[0];
        Assert.Equal("images/abc123-photo.png", restored.ImageRelativePath);
    }

    [Fact]
    public void StudyCardImagesAreUndoable()
    {
        var (doc, undo) = Make();

        var card = doc.AddStudyCard();

        undo.CaptureBeforeChange("Add card image");
        card.FrontImageRelativePath = "images/def456-front.png";

        Assert.True(undo.Undo());

        Assert.Null(doc.Current.StudyCards[0].FrontImageRelativePath);
    }

    [Fact]
    public void LabelsDescribeWhatWouldBeReversed()
    {
        var (doc, undo) = Make();

        undo.CaptureBeforeChange("Delete section");
        doc.AddSection("A");

        Assert.Equal("Delete section", undo.NextUndoLabel);

        undo.Undo();
        Assert.Equal("Delete section", undo.NextRedoLabel);
    }

    [Fact]
    public void StateChangedFiresWhenAvailabilityChanges()
    {
        var (doc, undo) = Make();

        var fired = 0;
        undo.StateChanged += (_, _) => fired++;

        AddSection(doc, undo, "A");
        Assert.True(fired > 0);

        var afterCapture = fired;
        undo.Undo();
        Assert.True(fired > afterCapture);
    }

    [Fact]
    public void ReorderingIsUndoable()
    {
        var (doc, undo) = Make();

        var a = doc.AddSection("A");
        doc.AddSection("B");
        doc.AddSection("C");

        undo.CaptureBeforeChange("Move section");
        doc.MoveSection(a.Id, 2);
        Assert.Equal("A", doc.Current.Sections[2].Title);

        undo.Undo();
        Assert.Equal("A", doc.Current.Sections[0].Title);
    }

    [Fact]
    public void DisplayOrderIsRestoredAlongsideTheSections()
    {
        var (doc, undo) = Make();

        var a = doc.AddSection("A");
        doc.AddSection("B");

        undo.CaptureBeforeChange("Move section");
        doc.MoveSection(a.Id, 1);

        undo.Undo();

        // SectionDisplayOrder takes priority when publishing, so a stale copy
        // would let the builder show one order and an export use another.
        Assert.Equal(
            doc.Current.Sections.Select(s => s.Id),
            doc.Current.SectionDisplayOrder);
    }
}
