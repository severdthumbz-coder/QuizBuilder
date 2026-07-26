using System.Windows;
using System.Windows.Media;
using QuizBuilder.Core.Theming;

namespace QuizBuilder.App.Theming;

/// <summary>
/// Bridges platform-neutral <see cref="ThemeTokens"/> into WPF resources.
///
/// This is the seam that lets Core stay WPF-free: the tokens are hex strings
/// and doubles, and this class is the only place that turns them into Brushes.
/// The PDF and HTML exporters will read the same tokens and make their own
/// representations, without a WPF dependency between them.
///
/// Resource keys are strings following the token path, e.g. "Color.Primary",
/// "Brush.Surface", "Font.Size.Body". XAML then uses
/// {DynamicResource Brush.Primary} so a theme switch updates live.
/// </summary>
public static class ThemeResourceBuilder
{
    /// <summary>
    /// Produces a dictionary of WPF resources for the given tokens.
    /// Every brush is frozen: frozen Freezables are cheaper and can be shared
    /// across threads without a copy, which matters when the same brush is
    /// referenced by hundreds of elements.
    /// </summary>
    public static ResourceDictionary Build(ThemeTokens tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        var d = new ResourceDictionary();

        AddColors(d, tokens.Colors);
        AddTypography(d, tokens.Typography);
        AddSpacing(d, tokens.Spacing);
        AddShape(d, tokens.Shape);
        AddMotion(d, tokens.Motion);

        return d;
    }

    private static void AddColors(ResourceDictionary d, ColorTokens c)
    {
        AddColorPair(d, "Background", c.Background);
        AddColorPair(d, "Surface", c.Surface);
        AddColorPair(d, "SurfaceRaised", c.SurfaceRaised);
        AddColorPair(d, "SurfaceSunken", c.SurfaceSunken);

        AddColorPair(d, "Primary", c.Primary);
        AddColorPair(d, "OnPrimary", c.OnPrimary);
        AddColorPair(d, "Accent", c.Accent);
        AddColorPair(d, "OnAccent", c.OnAccent);

        AddColorPair(d, "TextPrimary", c.TextPrimary);
        AddColorPair(d, "TextSecondary", c.TextSecondary);
        AddColorPair(d, "TextDisabled", c.TextDisabled);
        AddColorPair(d, "TextOnSurfaceInverse", c.TextOnSurfaceInverse);

        AddColorPair(d, "Border", c.Border);
        AddColorPair(d, "BorderStrong", c.BorderStrong);
        AddColorPair(d, "Divider", c.Divider);

        AddColorPair(d, "Success", c.Success);
        AddColorPair(d, "Warning", c.Warning);
        AddColorPair(d, "Error", c.Error);
        AddColorPair(d, "Info", c.Info);

        AddColorPair(d, "HoverOverlay", c.HoverOverlay);
        AddColorPair(d, "PressedOverlay", c.PressedOverlay);
        AddColorPair(d, "SelectedOverlay", c.SelectedOverlay);
        AddColorPair(d, "FocusRing", c.FocusRing);
        AddColorPair(d, "Scrim", c.Scrim);
    }

    /// <summary>
    /// Registers both the raw Color and a frozen SolidColorBrush. Both are
    /// needed: animations and gradient stops want a Color, while Background
    /// and Foreground want a Brush.
    /// </summary>
    private static void AddColorPair(ResourceDictionary d, string name, string hex)
    {
        var color = ParseColor(hex);
        d[$"Color.{name}"] = color;

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        d[$"Brush.{name}"] = brush;
    }

    /// <summary>
    /// Parses #RRGGBB or #RRGGBBAA.
    ///
    /// Note the ordering difference: the token format puts alpha LAST
    /// (CSS convention, #RRGGBBAA) because these strings are also emitted into
    /// the published HTML. WPF's ColorConverter expects alpha FIRST (#AARRGGBB).
    /// Parsing by hand avoids a silent misread where an overlay's alpha is
    /// interpreted as its red channel.
    /// </summary>
    internal static Color ParseColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            throw new FormatException("Colour token is empty.");

        var h = hex.TrimStart('#');

        return h.Length switch
        {
            6 => Color.FromRgb(
                    Hex(h, 0), Hex(h, 2), Hex(h, 4)),

            8 => Color.FromArgb(
                    Hex(h, 6),                       // alpha last in the token
                    Hex(h, 0), Hex(h, 2), Hex(h, 4)),

            _ => throw new FormatException(
                    $"Expected #RRGGBB or #RRGGBBAA, got '{hex}'.")
        };

        static byte Hex(string s, int i) =>
            byte.Parse(s.AsSpan(i, 2), System.Globalization.NumberStyles.HexNumber);
    }

    private static void AddTypography(ResourceDictionary d, TypographyTokens t)
    {
        // The token carries a CSS-style stack ("Georgia, Cambria, serif").
        // WPF's FontFamily accepts a comma-separated list and falls back
        // left-to-right, so the stack passes through directly.
        d["Font.Family"] = new FontFamily(t.FontFamily);
        d["Font.Family.Mono"] = new FontFamily(t.MonoFontFamily);

        d["Font.Size.Caption"] = t.Caption;
        d["Font.Size.Body"] = t.Body;
        d["Font.Size.Subtitle"] = t.Subtitle;
        d["Font.Size.Title"] = t.Title;
        d["Font.Size.Headline"] = t.Headline;
        d["Font.Size.Display"] = t.Display;

        d["Font.Weight.Regular"] = FontWeight.FromOpenTypeWeight(t.WeightRegular);
        d["Font.Weight.Medium"] = FontWeight.FromOpenTypeWeight(t.WeightMedium);
        d["Font.Weight.Bold"] = FontWeight.FromOpenTypeWeight(t.WeightBold);

        d["Font.LineHeight.Body"] = t.Body * t.LineHeightBody;
        d["Font.LineHeight.Heading"] = t.Title * t.LineHeightHeading;
    }

    private static void AddSpacing(ResourceDictionary d, SpacingTokens s)
    {
        d["Space.Xxs"] = s.Xxs;
        d["Space.Xs"] = s.Xs;
        d["Space.Sm"] = s.Sm;
        d["Space.Md"] = s.Md;
        d["Space.Lg"] = s.Lg;
        d["Space.Xl"] = s.Xl;
        d["Space.Xxl"] = s.Xxl;

        // Thickness variants, since XAML Margin/Padding want a Thickness and
        // cannot coerce from a double resource.
        d["Thickness.Xs"] = new Thickness(s.Xs);
        d["Thickness.Sm"] = new Thickness(s.Sm);
        d["Thickness.Md"] = new Thickness(s.Md);
        d["Thickness.Lg"] = new Thickness(s.Lg);
    }

    private static void AddShape(ResourceDictionary d, ShapeTokens s)
    {
        d["Radius.Sm"] = new CornerRadius(s.RadiusSm);
        d["Radius.Md"] = new CornerRadius(s.RadiusMd);
        d["Radius.Lg"] = new CornerRadius(s.RadiusLg);
        d["Radius.Pill"] = new CornerRadius(s.RadiusPill);

        d["Border.Width"] = new Thickness(s.BorderWidth);
        d["FocusRing.Width"] = s.FocusRingWidth;
        d["FocusRing.Offset"] = s.FocusRingOffset;
    }

    private static void AddMotion(ResourceDictionary d, MotionTokens m)
    {
        d["Duration.Fast"] = TimeSpan.FromMilliseconds(m.DurationFast);
        d["Duration.Normal"] = TimeSpan.FromMilliseconds(m.DurationNormal);
        d["Duration.Slow"] = TimeSpan.FromMilliseconds(m.DurationSlow);
    }
}
