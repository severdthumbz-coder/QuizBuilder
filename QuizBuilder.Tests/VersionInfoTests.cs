using QuizBuilder.Core;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// VersionInfo reads whatever the build stamped into the test assembly, so
/// these assert on shape and invariants rather than exact numbers -- the
/// values legitimately change every time version.json is bumped.
///
/// The parsing rules were validated against the exact strings build.bat
/// produces ("0.1.0+build.1" / "0.1.0.1") before this was written.
/// </summary>
public class VersionInfoTests
{
    [Fact]
    public void Semantic_IsThreePartNumeric()
    {
        var parts = VersionInfo.Semantic.Split('.');

        Assert.Equal(3, parts.Length);
        Assert.All(parts, p => Assert.True(int.TryParse(p, out _),
            $"'{VersionInfo.Semantic}' is not a three-part numeric version."));
    }

    [Fact]
    public void Semantic_StripsBuildMetadata()
    {
        // InformationalVersion carries "+build.N"; Semantic must not.
        Assert.DoesNotContain('+', VersionInfo.Semantic);
        Assert.DoesNotContain('-', VersionInfo.Semantic);
    }

    [Fact]
    public void Build_IsNonNegative()
    {
        Assert.True(VersionInfo.Build >= 0);
    }

    [Fact]
    public void Display_HasExpectedShape()
    {
        // "v0.1.0 (build 1)"
        Assert.StartsWith("v", VersionInfo.Display);
        Assert.Contains("(build ", VersionInfo.Display);
        Assert.EndsWith(")", VersionInfo.Display);
    }

    [Fact]
    public void FileNameSuffix_ContainsNoIllegalPathCharacters()
    {
        // This string goes straight into a zip filename, so an illegal
        // character here breaks the build script rather than the app.
        var illegal = Path.GetInvalidFileNameChars();

        Assert.DoesNotContain(VersionInfo.FileNameSuffix, c => illegal.Contains(c));
    }

    [Fact]
    public void FileNameSuffix_HasNoSpaces()
    {
        // Spaces are legal in filenames but make command lines fragile.
        Assert.DoesNotContain(' ', VersionInfo.FileNameSuffix);
    }

    [Fact]
    public void FileVersion_IsFourPartNumeric()
    {
        var parts = VersionInfo.FileVersion.Split('.');

        Assert.Equal(4, parts.Length);
        Assert.All(parts, p => Assert.True(int.TryParse(p, out _)));
    }

    [Fact]
    public void Informational_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(VersionInfo.Informational));
    }

    [Fact]
    public void DisplayAndFileNameSuffix_AgreeOnVersionAndBuild()
    {
        // Both are derived from the same source; if they disagree, the title
        // bar and the zip filename would name different builds.
        Assert.Contains(VersionInfo.Semantic, VersionInfo.Display);
        Assert.Contains(VersionInfo.Semantic, VersionInfo.FileNameSuffix);
        Assert.Contains(VersionInfo.Build.ToString(), VersionInfo.Display);
        Assert.Contains(VersionInfo.Build.ToString(), VersionInfo.FileNameSuffix);
    }
}
