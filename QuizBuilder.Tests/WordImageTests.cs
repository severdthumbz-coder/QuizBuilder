using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using QuizBuilder.Core.Theming;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// The Word export with images. These unzip the produced .docx and assert the
/// four parts agree -- content types, relationships, media, and the drawings in
/// the body -- because a mismatch there is what makes Word offer to "repair" the
/// file, and that cannot be caught by eye.
/// </summary>
public class WordImageTests
{
    private static readonly ThemeTokens Theme = BuiltInThemes.Academic();

    private static byte[] Png(int w, int h)
    {
        var sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        using var ms = new MemoryStream();
        ms.Write(sig);
        void BeInt(int v) => ms.Write(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });
        BeInt(13);
        ms.Write(new[] { (byte)'I', (byte)'H', (byte)'D', (byte)'R' });
        BeInt(w); BeInt(h);
        ms.Write(new byte[] { 0x08, 0x02, 0x00, 0x00, 0x00 });
        BeInt(0);
        return ms.ToArray();
    }

    private static MultipleChoiceSingleQuestion Q(string prompt, string? imagePath)
    {
        var q = new MultipleChoiceSingleQuestion { Prompt = prompt, Points = 1, ImageRelativePath = imagePath };
        q.Choices.Add(new Choice { Text = "a", IsCorrect = true });
        q.Choices.Add(new Choice { Text = "b" });
        return q;
    }

    private static ZipArchive Export(params (string prompt, string? path)[] questions)
    {
        var pkg = new QuizPackageService();

        // Register a distinct image for each unique path.
        var byPath = questions
            .Where(x => x.path is not null)
            .Select(x => x.path!)
            .Distinct()
            .ToDictionary(p => p, p => pkg.AddImage(Png(100, 60), p.EndsWith(".jpg") ? "x.jpg" : "x.png"));

        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "S" };
        foreach (var (prompt, path) in questions)
            section.Questions.Add(Q(prompt, path is null ? null : byPath[path]));
        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);

        var compiled = new QuizCompiler().Compile(doc, new QuizSettings(), seed: 0);

        var buffer = new MemoryStream();
        new WordExporter().Write(buffer, compiled, Theme, new WordExportOptions
        {
            ImageBytesResolver = pkg.GetImage,
        });

        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    private static string Read(ZipArchive zip, string path)
    {
        using var reader = new StreamReader(zip.GetEntry(path)!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void EveryEmbeddedIdResolvesToARelationship()
    {
        using var zip = Export(("has image", "images/a.png"));

        var doc = Read(zip, "word/document.xml");
        var rels = Read(zip, "word/_rels/document.xml.rels");

        var embeds = Regex.Matches(doc, "r:embed=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
        var relIds = Regex.Matches(rels, "Relationship Id=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToHashSet();

        Assert.NotEmpty(embeds);
        Assert.Subset(relIds, embeds);   // every embed id is present in the rels
    }

    [Fact]
    public void EachImageRelationshipTargetExistsAsMedia()
    {
        using var zip = Export(("q", "images/a.png"));

        var rels = Read(zip, "word/_rels/document.xml.rels");
        var targets = Regex.Matches(rels, "Target=\"media/([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();

        Assert.NotEmpty(targets);
        foreach (var target in targets)
            Assert.NotNull(zip.GetEntry($"word/media/{target}"));
    }

    [Fact]
    public void ContentTypesDeclareEachImageExtension()
    {
        using var zip = Export(("png q", "images/a.png"));

        var contentTypes = Read(zip, "[Content_Types].xml");

        Assert.Contains("Extension=\"png\"", contentTypes);
    }

    [Fact]
    public void AReusedImageProducesOneMediaFileButTwoDrawings()
    {
        // Same path on two questions: dedupe the media and the relationship, but
        // each question still gets its own drawing.
        using var zip = Export(("q1", "images/same.png"), ("q2", "images/same.png"));

        var doc = Read(zip, "word/document.xml");
        var mediaCount = zip.Entries.Count(e => e.FullName.StartsWith("word/media/"));
        var drawingCount = Regex.Matches(doc, "<w:drawing>").Count;

        Assert.Equal(1, mediaCount);
        Assert.Equal(2, drawingCount);
    }

    [Fact]
    public void DrawingDocPrIdsAreUniqueAndNonZero()
    {
        using var zip = Export(("q1", "images/a.png"), ("q2", "images/b.png"));

        var doc = Read(zip, "word/document.xml");
        var ids = Regex.Matches(doc, "<wp:docPr id=\"(\\d+)\"").Select(m => int.Parse(m.Groups[1].Value)).ToList();

        Assert.Equal(2, ids.Count);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.True(id > 0));
    }

    [Fact]
    public void AQuestionWithoutAnImageEmitsNoDrawing()
    {
        using var zip = Export(("no image", null));

        var doc = Read(zip, "word/document.xml");

        Assert.DoesNotContain("<w:drawing>", doc);
    }

    [Fact]
    public void WithNoResolverNoImagesAreEmbeddedAndTheDocIsStillValid()
    {
        var pkg = new QuizPackageService();
        var path = pkg.AddImage(Png(10, 10), "x.png");

        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "S" };
        section.Questions.Add(Q("q", path));
        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);

        var compiled = new QuizCompiler().Compile(doc, new QuizSettings(), seed: 0);

        var buffer = new MemoryStream();
        new WordExporter().Write(buffer, compiled, Theme, new WordExportOptions());   // no resolver
        buffer.Position = 0;

        using var zip = new ZipArchive(buffer, ZipArchiveMode.Read);
        var body = Read(zip, "word/document.xml");

        Assert.DoesNotContain("<w:drawing>", body);
        Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("word/media/"));
    }

    [Fact]
    public void AllPartsAreWellFormedXml()
    {
        using var zip = Export(("q", "images/a.png"));

        foreach (var part in new[] { "[Content_Types].xml", "word/_rels/document.xml.rels", "word/document.xml" })
        {
            var xml = Read(zip, part);
            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(xml);   // throws if malformed
        }
    }
}
