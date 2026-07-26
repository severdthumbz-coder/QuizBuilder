using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// A .qbx written by an older build must still open after the format version
/// was raised to 2 for the sequence question type.
///
/// <para>
/// The load gate only refuses the <i>future</i> (a file whose FormatVersion
/// exceeds what this build understands). A version-1 file is the past, so it
/// must load unchanged -- and its reported version must stay 1, since reading
/// an old file does not silently upgrade it. These tests hand-build a genuine
/// version-1 archive rather than round-tripping the current writer, because the
/// current writer only ever stamps version 2; nothing else would actually
/// exercise the older path.
/// </para>
/// </summary>
public class PackageBackwardCompatTests : System.IDisposable
{
    private readonly string _dir;
    private readonly QuizPackageService _package = new();

    public PackageBackwardCompatTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "qb-com_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>
    /// Writes a .qbx by hand with the given format version, serialising the quiz
    /// exactly as the real writer would (same JSON options, same entry names).
    /// </summary>
    private string WritePackage(int formatVersion, QuizDocument document,
        (string path, byte[] bytes)? image = null)
    {
        var path = Path.Combine(_dir, $"v{formatVersion}.qbx");

        using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);

        // manifest.json: only the fields the reader looks at. camelCase to match
        // SettingsService.JsonOptions, which is what the reader deserialises with.
        var manifest = new { formatVersion, appVersion = "0.22.0", savedUtc = System.DateTimeOffset.UtcNow };
        WriteEntry(archive, "manifest.json", JsonSerializer.Serialize(manifest));

        // quiz.json: the real document, through the real serializer, so this is a
        // faithful old file rather than an approximation.
        var quizJson = JsonSerializer.Serialize(document, SettingsService.JsonOptions);
        WriteEntry(archive, "quiz.json", quizJson);

        if (image is { } img)
        {
            var entry = archive.CreateEntry(img.path, CompressionLevel.Optimal);
            using var s = entry.Open();
            s.Write(img.bytes, 0, img.bytes.Length);
        }

        return path;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    /// <summary>A pre-0.25 quiz: only question types that existed at version 1.</summary>
    private static QuizDocument LegacyQuiz()
    {
        var doc = new QuizDocument { Title = "Legacy quiz", Description = "Made before sequences existed" };
        var section = new Section { Title = "Section 1" };

        var mc = new MultipleChoiceSingleQuestion { Prompt = "Capital of France?", Points = 2 };
        mc.Choices.Add(new Choice { Text = "Paris", IsCorrect = true });
        mc.Choices.Add(new Choice { Text = "Berlin" });
        section.Questions.Add(mc);

        var tf = new TrueFalseQuestion { Prompt = "Water is wet.", Points = 1, CorrectAnswer = true };
        section.Questions.Add(tf);

        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);
        return doc;
    }

    [Fact]
    public async Task AVersionOneFileStillOpens()
    {
        var path = WritePackage(1, LegacyQuiz());

        var result = await _package.LoadAsync(path);

        Assert.NotNull(result.Document);
        Assert.Equal("Legacy quiz", result.Document.Title);
    }

    [Fact]
    public async Task ReadingAnOldFileDoesNotUpgradeItsReportedVersion()
    {
        // The reader surfaces the version the file was written at. Reading is not
        // a migration, so an opened v1 file still reports 1 -- it only becomes a
        // v2 file if the user saves it again.
        var path = WritePackage(1, LegacyQuiz());

        var result = await _package.LoadAsync(path);

        Assert.Equal(1, result.FormatVersion);
    }

    [Fact]
    public async Task AVersionOneFilesContentSurvivesUnchanged()
    {
        var path = WritePackage(1, LegacyQuiz());

        var doc = (await _package.LoadAsync(path)).Document;
        var questions = doc.Sections.SelectMany(s => s.Questions).ToList();

        Assert.Equal(2, questions.Count);

        var mc = Assert.IsType<MultipleChoiceSingleQuestion>(questions[0]);
        Assert.Equal("Capital of France?", mc.Prompt);
        Assert.Equal("Paris", mc.Choices.Single(c => c.IsCorrect).Text);

        var tf = Assert.IsType<TrueFalseQuestion>(questions[1]);
        Assert.True(tf.CorrectAnswer);
    }

    [Fact]
    public async Task AVersionOneFileWithAnImageStillResolvesIt()
    {
        // A tiny 1x1 PNG stored under images/ must come back attached, proving
        // the image-loading path is unaffected by the version bump.
        var png = new byte[]
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
            0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
            0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
            0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
            0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
            0x42, 0x60, 0x82,
        };

        var doc = LegacyQuiz();
        const string imagePath = "images/abc123.png";
        doc.Sections[0].Questions[0].ImageRelativePath = imagePath;

        var path = WritePackage(1, doc, (imagePath, png));

        var result = await _package.LoadAsync(path);
        var loadedQuestion = result.Document.Sections[0].Questions[0];

        // The reference survived (a dangling reference would have been cleared
        // with a warning), so the image resolved.
        Assert.Equal(imagePath, loadedQuestion.ImageRelativePath);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("missing image"));
        Assert.Equal(png, _package.GetImage(imagePath));
    }

    [Fact]
    public async Task ACurrentSaveIsStampedVersionTwo()
    {
        // The other side of the gate: a file this build writes carries the new
        // version, so a still-older build would correctly refuse it.
        var path = Path.Combine(_dir, "current.qbx");
        await _package.SaveAsync(LegacyQuiz(), path);

        var result = await _package.LoadAsync(path);

        Assert.Equal(_package.CurrentFormatVersion, result.FormatVersion);
        Assert.True(result.FormatVersion >= 2);
    }
}
