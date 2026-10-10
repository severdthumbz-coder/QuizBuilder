using System.Text.Json;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Crash recovery. The state machine was modelled and proved in
/// tools/port/crash_recovery_port.py first; these pin the same behaviour through
/// the real C# CrashRecoveryService, plus the document JSON round-trip and the
/// RestoreRecoveredDocument path.
/// </summary>
public class CrashRecoveryTests
{
    private static QuizDocument SampleDoc(string title)
    {
        var doc = new QuizDocument { Title = title };
        var section = new Section { Title = "S" };
        section.Questions.Add(new ShortAnswerQuestion { Prompt = "Q" });
        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);
        return doc;
    }

    // ----- Snapshot presence is the crash signal -------------------------- //

    [Fact]
    public void NoSnapshotMeansNothingToRecover()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var svc = new CrashRecoveryService(dir);
            Assert.False(svc.HasPendingSnapshot);
            Assert.Null(svc.TryLoadPendingSnapshot());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CaptureThenReopenSeesThePendingSnapshot()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            new CrashRecoveryService(dir).CaptureSnapshot(SampleDoc("Crashy"), originalPath: null);

            // A fresh instance (next launch) finds it.
            var reopened = new CrashRecoveryService(dir);
            Assert.True(reopened.HasPendingSnapshot);
            var snap = reopened.TryLoadPendingSnapshot();
            Assert.NotNull(snap);
            Assert.Null(snap!.OriginalPath);           // never saved -> recovered anyway
            Assert.False(string.IsNullOrEmpty(snap.DocumentJson));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ClearRemovesTheSnapshot()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var svc = new CrashRecoveryService(dir);
            svc.CaptureSnapshot(SampleDoc("x"), null);
            Assert.True(svc.HasPendingSnapshot);

            svc.Clear();
            Assert.False(svc.HasPendingSnapshot);
            Assert.Null(new CrashRecoveryService(dir).TryLoadPendingSnapshot());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void ClearIsIdempotentWhenNothingToClear()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var svc = new CrashRecoveryService(dir);
            svc.Clear();                 // no snapshot: must not throw
            Assert.False(svc.HasPendingSnapshot);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ----- The captured document round-trips ------------------------------ //

    [Fact]
    public void SnapshotPreservesTheDocumentAndPath()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var original = SampleDoc("Round Trip Quiz");
            new CrashRecoveryService(dir).CaptureSnapshot(original, originalPath: @"C:\quizzes\exam.qbx");

            var snap = new CrashRecoveryService(dir).TryLoadPendingSnapshot();
            Assert.NotNull(snap);
            Assert.Equal(@"C:\quizzes\exam.qbx", snap!.OriginalPath);

            // The document JSON deserializes back to the same content.
            var restored = JsonSerializer.Deserialize<QuizDocument>(snap.DocumentJson, SettingsService.JsonOptions);
            Assert.NotNull(restored);
            Assert.Equal("Round Trip Quiz", restored!.Title);
            Assert.Single(restored.Sections);
            Assert.Equal(original.Id, restored.Id);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void CapturingAgainReplacesThePreviousSnapshot()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var svc = new CrashRecoveryService(dir);
            svc.CaptureSnapshot(SampleDoc("first"), null);
            svc.CaptureSnapshot(SampleDoc("second"), null);

            var snap = new CrashRecoveryService(dir).TryLoadPendingSnapshot();
            var restored = JsonSerializer.Deserialize<QuizDocument>(snap!.DocumentJson, SettingsService.JsonOptions);
            Assert.Equal("second", restored!.Title);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ----- Corrupt tolerance ---------------------------------------------- //

    [Fact]
    public void CorruptSnapshotIsDiscardedNotThrown()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "recovery-snapshot.json"), "{ not valid json");
            var svc = new CrashRecoveryService(dir);

            Assert.Null(svc.TryLoadPendingSnapshot());   // no throw
            Assert.False(svc.HasPendingSnapshot);        // discarded
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void StructurallyValidButEmptySnapshotIsTreatedAsNothingToRecover()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            // Valid JSON, but no document payload.
            File.WriteAllText(Path.Combine(dir, "recovery-snapshot.json"), "{\"Schema\":1,\"DocumentJson\":\"\"}");
            var svc = new CrashRecoveryService(dir);

            Assert.Null(svc.TryLoadPendingSnapshot());
            Assert.False(svc.HasPendingSnapshot);
        }
        finally { Directory.Delete(dir, true); }
    }

    // ----- TryRecoverDocument deserializes in Core ------------------------ //

    [Fact]
    public void TryRecoverDocumentReturnsDocumentAndPath()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            new CrashRecoveryService(dir).CaptureSnapshot(SampleDoc("Recover Me"), @"C:\q\e.qbx");

            var recovered = new CrashRecoveryService(dir).TryRecoverDocument();
            Assert.NotNull(recovered);
            Assert.Equal("Recover Me", recovered!.Value.Document.Title);
            Assert.Equal(@"C:\q\e.qbx", recovered.Value.OriginalPath);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void TryRecoverDocumentReturnsNullWhenNothingPending()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            Assert.Null(new CrashRecoveryService(dir).TryRecoverDocument());
        }
        finally { Directory.Delete(dir, true); }
    }

    // ----- No stray temp files left behind -------------------------------- //

    [Fact]
    public void AtomicWriteLeavesNoTempFiles()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var svc = new CrashRecoveryService(dir);
            svc.CaptureSnapshot(SampleDoc("x"), null);
            svc.CaptureSnapshot(SampleDoc("y"), null);

            var temps = Directory.GetFiles(dir, ".recovery-*.tmp");
            Assert.Empty(temps);   // the move consumed every temp
        }
        finally { Directory.Delete(dir, true); }
    }

    // ----- RestoreRecoveredDocument marks dirty + reattaches path ---------- //

    [Fact]
    public void RestoreRecoveredDocumentLoadsDirtyWithPath()
    {
        var svc = new QuizDocumentService();
        var doc = SampleDoc("Recovered");

        svc.RestoreRecoveredDocument(doc, @"C:\quizzes\exam.qbx");

        Assert.Same(doc, svc.Current);
        Assert.Equal(@"C:\quizzes\exam.qbx", svc.CurrentFilePath);
        Assert.True(svc.IsDirty);   // recovered work is unsaved against that file
    }

    [Fact]
    public void RestoreRecoveredNeverSavedDocumentHasNoPathButIsDirty()
    {
        var svc = new QuizDocumentService();
        var doc = SampleDoc("Never Saved");

        svc.RestoreRecoveredDocument(doc, filePath: null);

        Assert.Same(doc, svc.Current);
        Assert.Null(svc.CurrentFilePath);
        Assert.True(svc.IsDirty);
    }
}
