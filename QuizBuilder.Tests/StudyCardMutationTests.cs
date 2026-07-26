using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Study-card mutations on the document service: add, update (with no-op
/// coalescing), remove, and move, each raising StudyCardsChanged.
/// </summary>
public class StudyCardMutationTests
{
    private static (QuizDocumentService svc, System.Collections.Generic.List<DocumentChangeKind> events) Svc()
    {
        var svc = new QuizDocumentService();
        var events = new System.Collections.Generic.List<DocumentChangeKind>();
        svc.DocumentChanged += (_, e) => events.Add(e.Kind);
        return (svc, events);
    }

    [Fact]
    public void AddAppendsAndRaises()
    {
        var (svc, events) = Svc();

        var card = svc.AddStudyCard();

        Assert.Single(svc.Current.StudyCards);
        Assert.Equal(card.Id, svc.Current.StudyCards[0].Id);
        Assert.Contains(DocumentChangeKind.StudyCardsChanged, events);
    }

    [Fact]
    public void UpdateChangesText()
    {
        var (svc, _) = Svc();
        var card = svc.AddStudyCard();

        svc.UpdateStudyCard(card.Id, "Front text", "Back text");

        Assert.Equal("Front text", svc.Current.StudyCards[0].Front);
        Assert.Equal("Back text", svc.Current.StudyCards[0].Back);
    }

    [Fact]
    public void UpdateWithIdenticalTextIsANoOp()
    {
        var (svc, events) = Svc();
        var card = svc.AddStudyCard();
        svc.UpdateStudyCard(card.Id, "a", "b");

        events.Clear();
        svc.UpdateStudyCard(card.Id, "a", "b");   // same text again

        // No event: coalescing keeps per-keystroke edits from churning.
        Assert.Empty(events);
    }

    [Fact]
    public void UpdateOnMissingCardDoesNothing()
    {
        var (svc, events) = Svc();
        svc.AddStudyCard();
        events.Clear();

        svc.UpdateStudyCard(System.Guid.NewGuid(), "x", "y");

        Assert.Empty(events);
    }

    [Fact]
    public void RemoveDropsTheCard()
    {
        var (svc, _) = Svc();
        var a = svc.AddStudyCard();
        var b = svc.AddStudyCard();

        svc.RemoveStudyCard(a.Id);

        Assert.Single(svc.Current.StudyCards);
        Assert.Equal(b.Id, svc.Current.StudyCards[0].Id);
    }

    [Fact]
    public void MoveReordersTheList()
    {
        var (svc, _) = Svc();
        var a = svc.AddStudyCard();
        var b = svc.AddStudyCard();
        var c = svc.AddStudyCard();

        svc.MoveStudyCard(c.Id, 0);   // move last to first

        Assert.Equal(new[] { c.Id, a.Id, b.Id },
            svc.Current.StudyCards.ConvertAll(x => x.Id).ToArray());
    }

    [Fact]
    public void MoveClampsOutOfRangeIndex()
    {
        var (svc, _) = Svc();
        var a = svc.AddStudyCard();
        var b = svc.AddStudyCard();

        svc.MoveStudyCard(a.Id, 99);   // past the end -> clamps to last

        Assert.Equal(new[] { b.Id, a.Id },
            svc.Current.StudyCards.ConvertAll(x => x.Id).ToArray());
    }

    [Fact]
    public void MoveToSamePositionIsANoOp()
    {
        var (svc, events) = Svc();
        var a = svc.AddStudyCard();
        svc.AddStudyCard();
        events.Clear();

        svc.MoveStudyCard(a.Id, 0);   // already at 0

        Assert.Empty(events);
    }
}
