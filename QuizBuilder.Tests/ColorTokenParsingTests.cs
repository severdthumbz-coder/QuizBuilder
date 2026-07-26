using QuizBuilder.Core.Theming;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// The theme tokens store colour as CSS-style #RRGGBBAA (alpha LAST), because
/// the same strings are emitted into the published HTML. WPF's built-in
/// ColorConverter expects #AARRGGBB (alpha FIRST).
///
/// That mismatch is why ThemeResourceBuilder parses by hand. It is also a
/// silent failure: fed to the wrong parser, the Academic hover overlay
/// #1F3A5F14 (navy at 8%) becomes rgb(58,95,20) at 12% -- a murky green. It
/// renders, it just looks wrong, and nothing logs an error.
///
/// These tests live in Core-facing test code and validate the token FORMAT.
/// The WPF-side conversion is verified by the probe window, which cannot be
/// unit-tested here because Core does not reference WPF.
/// </summary>
public class ColorTokenParsingTests
{
    /// <summary>
    /// Reference implementation of the token format, mirroring
    /// ThemeResourceBuilder.ParseColor. Kept here so the format contract is
    /// asserted even though the WPF converter itself lives in the App project.
    /// </summary>
    private static (byte A, byte R, byte G, byte B) ParseToken(string hex)
    {
        var h = hex.TrimStart('#');
        return h.Length switch
        {
            6 => ((byte)255, Hex(h, 0), Hex(h, 2), Hex(h, 4)),
            8 => (Hex(h, 6), Hex(h, 0), Hex(h, 2), Hex(h, 4)),
            _ => throw new FormatException($"Bad token: {hex}")
        };

        static byte Hex(string s, int i) =>
            byte.Parse(s.AsSpan(i, 2), System.Globalization.NumberStyles.HexNumber);
    }

    [Fact]
    public void SixDigitToken_IsFullyOpaque()
    {
        var (a, r, g, b) = ParseToken("#1F3A5F");

        Assert.Equal(255, a);
        Assert.Equal(0x1F, r);
        Assert.Equal(0x3A, g);
        Assert.Equal(0x5F, b);
    }

    [Fact]
    public void EightDigitToken_ReadsAlphaLast()
    {
        // Academic's SelectedOverlay: navy at 12%.
        var (a, r, g, b) = ParseToken("#1F3A5F1F");

        Assert.Equal(0x1F, a);   // alpha from the LAST pair
        Assert.Equal(0x1F, r);
        Assert.Equal(0x3A, g);
        Assert.Equal(0x5F, b);   // blue is NOT the alpha
    }

    [Fact]
    public void EveryOverlayToken_HasPlausibleAlpha()
    {
        // Overlays should be subtle (under ~25%); scrims are the exception
        // and are checked separately.
        foreach (var theme in BuiltInThemes.All)
        {
            foreach (var (name, token) in new[]
            {
                ("HoverOverlay", theme.Colors.HoverOverlay),
                ("PressedOverlay", theme.Colors.PressedOverlay),
                ("SelectedOverlay", theme.Colors.SelectedOverlay),
            })
            {
                var (a, _, _, _) = ParseToken(token);
                var pct = a / 255.0;

                Assert.True(pct > 0 && pct < 0.30,
                    $"[{theme.Id}] {name} = {token} -> {pct:P0} alpha. " +
                    $"An overlay this opaque suggests alpha was parsed from the " +
                    $"wrong position.");
            }
        }
    }

    [Fact]
    public void EveryScrim_IsHeavyEnoughToDim()
    {
        foreach (var theme in BuiltInThemes.All)
        {
            var (a, _, _, _) = ParseToken(theme.Colors.Scrim);
            var pct = a / 255.0;

            Assert.True(pct >= 0.50,
                $"[{theme.Id}] scrim {theme.Colors.Scrim} -> {pct:P0} alpha; " +
                $"too light to dim content behind a modal.");
        }
    }

    [Fact]
    public void EverySolidColorToken_IsSixDigits()
    {
        // Only overlays and scrims carry alpha. A stray 8-digit value in a
        // solid role would render semi-transparent and look like a rendering
        // bug rather than a data bug.
        foreach (var theme in BuiltInThemes.All)
        {
            var c = theme.Colors;
            foreach (var (name, token) in new[]
            {
                ("Background", c.Background), ("Surface", c.Surface),
                ("SurfaceRaised", c.SurfaceRaised), ("SurfaceSunken", c.SurfaceSunken),
                ("Primary", c.Primary), ("OnPrimary", c.OnPrimary),
                ("Accent", c.Accent), ("OnAccent", c.OnAccent),
                ("TextPrimary", c.TextPrimary), ("TextSecondary", c.TextSecondary),
                ("Border", c.Border), ("BorderStrong", c.BorderStrong),
                ("Success", c.Success), ("Warning", c.Warning),
                ("Error", c.Error), ("Info", c.Info),
                ("FocusRing", c.FocusRing),
            })
            {
                Assert.True(token.TrimStart('#').Length == 6,
                    $"[{theme.Id}] {name} = {token} should be #RRGGBB with no alpha.");
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("nonsense")]
    public void MalformedToken_Throws(string bad)
    {
        Assert.ThrowsAny<Exception>(() => ParseToken(bad));
    }
}
