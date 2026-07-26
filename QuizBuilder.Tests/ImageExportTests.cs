using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using QuizBuilder.Core.Theming;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Images in the single-file exports (HTML and self-grading web) and the
/// data-URI resolver they rely on.
/// </summary>
public class ImageExportTests
{
    private static readonly ThemeTokens Theme = BuiltInThemes.Academic();

    // A 1x1 transparent PNG.
    private static readonly byte[] Png =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
    };

    private static (CompiledQuiz quiz, string imagePath, QuizPackageService pkg) CompileWithImage()
    {
        var pkg = new QuizPackageService();
        var imagePath = pkg.AddImage(Png, "diagram.png");

        var q = new MultipleChoiceSingleQuestion { Prompt = "What is this?", Points = 1, ImageRelativePath = imagePath };
        q.Choices.Add(new Choice { Text = "wrong" });
        q.Choices.Add(new Choice { Text = "right", IsCorrect = true });

        var doc = new QuizDocument { Title = "T" };
        var section = new Section { Title = "S" };
        section.Questions.Add(q);
        doc.Sections.Add(section);
        doc.SectionDisplayOrder.Add(section.Id);

        return (new QuizCompiler().Compile(doc, new QuizSettings(), seed: 0), imagePath, pkg);
    }

    [Fact]
    public void DataUriHasTheRightMimeAndDecodesBack()
    {
        var pkg = new QuizPackageService();
        var path = pkg.AddImage(Png, "x.png");

        var uri = pkg.GetImageDataUri(path);

        Assert.NotNull(uri);
        Assert.StartsWith("data:image/png;base64,", uri);

        var b64 = uri!.Substring("data:image/png;base64,".Length);
        Assert.Equal(Png, System.Convert.FromBase64String(b64));
    }

    [Fact]
    public void DataUriIsNullForAMissingImage()
    {
        var pkg = new QuizPackageService();

        Assert.Null(pkg.GetImageDataUri("images/does-not-exist.png"));
        Assert.Null(pkg.GetImageDataUri(null));
    }

    [Fact]
    public void HtmlExportEmbedsTheImageAsADataUri()
    {
        var (quiz, _, pkg) = CompileWithImage();

        var html = new HtmlExporter().Render(quiz, Theme, new HtmlExportOptions
        {
            ImageDataUriResolver = pkg.GetImageDataUri,
        });

        Assert.Contains("<img class=\"question-image\" src=\"data:image/png;base64,", html);
    }

    [Fact]
    public void HtmlExportOmitsTheImageWhenNoResolverIsGiven()
    {
        var (quiz, _, _) = CompileWithImage();

        // No resolver: the exporter cannot turn a path into bytes, so it simply
        // renders the question without the picture rather than emitting a broken
        // reference.
        var html = new HtmlExporter().Render(quiz, Theme, new HtmlExportOptions());

        Assert.DoesNotContain("<img", html);
    }

    [Fact]
    public void WebExportEmbedsTheImageAsADataUri()
    {
        var (quiz, _, pkg) = CompileWithImage();

        var html = new QuizWebExporter().Render(quiz, Theme, new WebExportOptions
        {
            ImageDataUriResolver = pkg.GetImageDataUri,
        });

        Assert.Contains("<img class=\"q-image\" src=\"data:image/png;base64,", html);
    }

    [Fact]
    public void AnImagePathThatResolvesToNullLeavesNoImgTag()
    {
        var (quiz, _, _) = CompileWithImage();

        // Resolver present but returns null (image not in this package): no <img>.
        var html = new HtmlExporter().Render(quiz, Theme, new HtmlExportOptions
        {
            ImageDataUriResolver = _ => null,
        });

        Assert.DoesNotContain("<img", html);
    }
}
