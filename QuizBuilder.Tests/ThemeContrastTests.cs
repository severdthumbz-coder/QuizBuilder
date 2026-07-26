using System.Globalization;
using QuizBuilder.Core.Theming;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Turns the accessibility promise into something a build can fail on.
///
/// Every one of these ratios was computed and checked before the palettes were
/// committed -- two real defects turned up that way (Playful's warning colour
/// sat at 3.58:1, and four themes had control borders below 3:1). Without this
/// test, the next person to "just tweak a colour" reintroduces them silently.
/// </summary>
public class ThemeContrastTests
{
    private const double TextMinimum = 4.5;      // WCAG AA, normal text
    private const double NonTextMinimum = 3.0;   // WCAG AA, UI components

    public static TheoryData<string> ThemeIds()
    {
        var data = new TheoryData<string>();
        foreach (var theme in BuiltInThemes.All) data.Add(theme.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void BodyText_MeetsContrastOnEverySurface(string themeId)
    {
        var c = BuiltInThemes.ById(themeId).Colors;

        AssertRatio(c.TextPrimary, c.Surface, TextMinimum, themeId, "TextPrimary on Surface");
        AssertRatio(c.TextPrimary, c.Background, TextMinimum, themeId, "TextPrimary on Background");
        AssertRatio(c.TextPrimary, c.SurfaceSunken, TextMinimum, themeId, "TextPrimary on SurfaceSunken");
        AssertRatio(c.TextPrimary, c.SurfaceRaised, TextMinimum, themeId, "TextPrimary on SurfaceRaised");
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void SecondaryText_MeetsContrast(string themeId)
    {
        var c = BuiltInThemes.ById(themeId).Colors;

        AssertRatio(c.TextSecondary, c.Surface, TextMinimum, themeId, "TextSecondary on Surface");
        AssertRatio(c.TextSecondary, c.Background, TextMinimum, themeId, "TextSecondary on Background");
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void TextOnBrandColors_MeetsContrast(string themeId)
    {
        var c = BuiltInThemes.ById(themeId).Colors;

        AssertRatio(c.OnPrimary, c.Primary, TextMinimum, themeId, "OnPrimary on Primary");
        AssertRatio(c.OnAccent, c.Accent, TextMinimum, themeId, "OnAccent on Accent");
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void StatusColors_AreLegibleOnSurface(string themeId)
    {
        var c = BuiltInThemes.ById(themeId).Colors;

        // This is the check that caught Playful's warning orange at 3.58:1.
        AssertRatio(c.Success, c.Surface, TextMinimum, themeId, "Success on Surface");
        AssertRatio(c.Warning, c.Surface, TextMinimum, themeId, "Warning on Surface");
        AssertRatio(c.Error, c.Surface, TextMinimum, themeId, "Error on Surface");
        AssertRatio(c.Info, c.Surface, TextMinimum, themeId, "Info on Surface");
    }

    [Theory]
    [MemberData(nameof(ThemeIds))]
    public void ControlBoundariesAndFocusRing_MeetNonTextContrast(string themeId)
    {
        var c = BuiltInThemes.ById(themeId).Colors;

        // BorderStrong delineates actual controls, so it is a meaningful
        // non-text element and must hit 3:1. Border/Divider are decorative
        // and deliberately exempt -- holding them to 3:1 would force black
        // hairlines everywhere.
        AssertRatio(c.BorderStrong, c.Surface, NonTextMinimum, themeId, "BorderStrong on Surface");
        AssertRatio(c.BorderStrong, c.Background, NonTextMinimum, themeId, "BorderStrong on Background");

        AssertRatio(c.FocusRing, c.Surface, NonTextMinimum, themeId, "FocusRing on Surface");
        AssertRatio(c.FocusRing, c.Background, NonTextMinimum, themeId, "FocusRing on Background");
    }

    [Fact]
    public void AllBuiltInThemes_HaveUniqueIds()
    {
        var ids = BuiltInThemes.All.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void AllBuiltInThemes_ArePresent()
    {
        Assert.Equal(5, BuiltInThemes.All.Count);
    }

    [Fact]
    public void ById_FallsBackToAcademic_ForUnknownId()
    {
        Assert.Equal(BuiltInThemes.AcademicId, BuiltInThemes.ById("does-not-exist").Id);
    }

    private static void AssertRatio(
        string foreground, string background, double minimum, string themeId, string label)
    {
        var actual = ContrastRatio(foreground, background);
        Assert.True(
            actual >= minimum,
            $"[{themeId}] {label}: {foreground} on {background} = " +
            $"{actual.ToString("F2", CultureInfo.InvariantCulture)}:1, " +
            $"needs {minimum.ToString("F1", CultureInfo.InvariantCulture)}:1");
    }

    /// <summary>WCAG 2.1 relative-luminance contrast ratio.</summary>
    internal static double ContrastRatio(string hexA, string hexB)
    {
        var la = RelativeLuminance(hexA);
        var lb = RelativeLuminance(hexB);
        var (hi, lo) = la > lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var h = hex.TrimStart('#');

        // Tokens may carry an alpha suffix (#RRGGBBAA) for overlays; the
        // luminance of the base colour is what matters here.
        if (h.Length is not (6 or 8))
            throw new FormatException($"Expected #RRGGBB or #RRGGBBAA, got '{hex}'.");

        var r = Channel(h.Substring(0, 2));
        var g = Channel(h.Substring(2, 2));
        var b = Channel(h.Substring(4, 2));

        return 0.2126 * r + 0.7152 * g + 0.0722 * b;

        static double Channel(string pair)
        {
            var v = int.Parse(pair, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
    }
}
