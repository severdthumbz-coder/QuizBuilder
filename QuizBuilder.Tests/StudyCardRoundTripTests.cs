using System.IO;
using System.Threading.Tasks;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Study cards must survive a .qbx save and reopen, like everything else on the
/// document. They are a plain list on QuizDocument, so this is really a check
/// that nothing special is needed -- but a feature that silently loses the
/// user's cards on save would be worse than not having it.
/// </summary>
public class StudyCardRoundTripTests : System.IDisposable
{
    private readonly string _dir;
    private readonly QuizPackageService _package = new();

    public StudyCardRoundTripTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "qb-study-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task StudyCardsSurviveSaveAndReopen()
    {
        var path = Path.Combine(_dir, "cards.qbx");

        var doc = new QuizDocument { Title = "With study cards" };
        doc.StudyCards.Add(new StudyCard { Front = "Capital of France", Back = "Paris" });
        doc.StudyCards.Add(new StudyCard { Front = "H2O", Back = "Water" });

        await _package.SaveAsync(doc, path);
        var reloaded = (await _package.LoadAsync(path)).Document;

        Assert.Equal(2, reloaded.StudyCards.Count);
        Assert.Equal("Capital of France", reloaded.StudyCards[0].Front);
        Assert.Equal("Paris", reloaded.StudyCards[0].Back);
        Assert.Equal("H2O", reloaded.StudyCards[1].Front);
        Assert.Equal("Water", reloaded.StudyCards[1].Back);
    }

    [Fact]
    public async Task StudyCardIdsSurviveSoOrderAndIdentityHold()
    {
        var path = Path.Combine(_dir, "ids.qbx");

        var doc = new QuizDocument { Title = "T" };
        var first = new StudyCard { Front = "a", Back = "1" };
        var second = new StudyCard { Front = "b", Back = "2" };
        doc.StudyCards.Add(first);
        doc.StudyCards.Add(second);

        await _package.SaveAsync(doc, path);
        var reloaded = (await _package.LoadAsync(path)).Document;

        Assert.Equal(first.Id, reloaded.StudyCards[0].Id);
        Assert.Equal(second.Id, reloaded.StudyCards[1].Id);
    }

    [Fact]
    public async Task ADocumentWithNoStudyCardsReopensWithAnEmptyList()
    {
        // An older .qbx written before study cards existed has no such key. It
        // must reopen with an empty list, not null -- the default initialiser
        // and System.Text.Json's handling of a missing property both give that,
        // but the feature relies on it, so pin it.
        var path = Path.Combine(_dir, "none.qbx");

        var doc = new QuizDocument { Title = "No cards" };
        await _package.SaveAsync(doc, path);
        var reloaded = (await _package.LoadAsync(path)).Document;

        Assert.NotNull(reloaded.StudyCards);
        Assert.Empty(reloaded.StudyCards);
    }

    [Fact]
    public async Task StudyCardImagesSurviveSaveAndReopen()
    {
        var path = Path.Combine(_dir, "cardimg.qbx");

        var doc = new QuizDocument { Title = "T" };
        var frontPng = new byte[] { 1, 2, 3, 4 };
        var backPng = new byte[] { 5, 6, 7, 8 };

        var frontPath = _package.AddImage(frontPng, "front.png");
        var backPath = _package.AddImage(backPng, "back.png");

        doc.StudyCards.Add(new StudyCard
        {
            Front = "identify", Back = "the answer",
            FrontImageRelativePath = frontPath,
            BackImageRelativePath = backPath,
        });

        await _package.SaveAsync(doc, path);

        // Fresh service so nothing is served from the in-memory cache.
        var fresh = new QuizPackageService();
        var reloaded = (await fresh.LoadAsync(path)).Document;

        Assert.Equal(frontPath, reloaded.StudyCards[0].FrontImageRelativePath);
        Assert.Equal(backPath, reloaded.StudyCards[0].BackImageRelativePath);
        Assert.Equal(frontPng, fresh.GetImage(frontPath));
        Assert.Equal(backPng, fresh.GetImage(backPath));
    }

    [Fact]
    public async Task AStudyCardImageThatIsNoLongerReferencedIsPruned()
    {
        var path = Path.Combine(_dir, "prune.qbx");

        var doc = new QuizDocument { Title = "T" };
        var orphan = _package.AddImage(new byte[] { 9, 9, 9 }, "orphan.png");
        var kept = _package.AddImage(new byte[] { 1, 1, 1 }, "kept.png");

        // Only 'kept' is referenced by a card.
        doc.StudyCards.Add(new StudyCard { Front = "x", FrontImageRelativePath = kept });

        await _package.SaveAsync(doc, path);

        var fresh = new QuizPackageService();
        await fresh.LoadAsync(path);

        // The referenced image is in the archive; the orphan was never written.
        Assert.NotNull(fresh.GetImage(kept));
        Assert.Null(fresh.GetImage(orphan));
    }

}
