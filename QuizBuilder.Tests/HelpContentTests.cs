using QuizBuilder.Core;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// The Help tab claims what the app can do. A feature list that drifts from
/// reality is worse than none, so the invariants that keep it honest are
/// asserted here rather than trusted.
///
/// These test the CONTENT rules, not the WPF view: QuizBuilder.Tests targets
/// net8.0 and cannot reference the WPF assembly.
/// </summary>
public class HelpContentTests
{
    [Fact]
    public void VersionInfo_AgreesWithWhatHelpWouldDisplay()
    {
        // Help shows VersionInfo.Display; the title bar and zip name derive
        // from the same source. If they disagree the app lies about itself.
        Assert.Contains(VersionInfo.Semantic, VersionInfo.Display);
        Assert.Contains(VersionInfo.Build.ToString(), VersionInfo.Display);
    }

    [Fact]
    public void SemanticVersion_ParsesAsThreeParts()
    {
        var parts = VersionInfo.Semantic.Split('.');
        Assert.Equal(3, parts.Length);
        Assert.All(parts, p => Assert.True(int.TryParse(p, out _)));
    }
}
