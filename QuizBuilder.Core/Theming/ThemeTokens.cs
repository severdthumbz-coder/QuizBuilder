using System.Text.Json.Serialization;

namespace QuizBuilder.Core.Theming;

/// <summary>
/// Platform-neutral design tokens. Deliberately contains NO WPF types:
/// this same object is consumed by the WPF resource layer, the PDF exporter
/// and the HTML emitter. Colors are hex strings; sizes are doubles (DIP/pt).
/// </summary>
public sealed class ThemeTokens
{
    public string Id { get; set; } = "academic";
    public string DisplayName { get; set; } = "Academic";
    public bool IsDark { get; set; }
    public bool IsBuiltIn { get; set; } = true;

    public ColorTokens Colors { get; set; } = new();
    public TypographyTokens Typography { get; set; } = new();
    public SpacingTokens Spacing { get; set; } = new();
    public ShapeTokens Shape { get; set; } = new();
    public MotionTokens Motion { get; set; } = new();

    /// <summary>Optional user logo, stored as a path relative to the .qbx package root.</summary>
    public string? LogoRelativePath { get; set; }

    public ThemeTokens Clone() => new()
    {
        Id = Id,
        DisplayName = DisplayName,
        IsDark = IsDark,
        IsBuiltIn = IsBuiltIn,
        LogoRelativePath = LogoRelativePath,
        Colors = Colors.Clone(),
        Typography = Typography.Clone(),
        Spacing = Spacing.Clone(),
        Shape = Shape.Clone(),
        Motion = Motion.Clone(),
    };
}

/// <summary>
/// Semantic color roles. Components must never reference a raw hex value;
/// they bind to a role. Adding a role here is cheap, renaming one is not.
/// </summary>
public sealed class ColorTokens
{
    // Surfaces, back to front.
    public string Background { get; set; } = "#F5F3EE";
    public string Surface { get; set; } = "#FFFFFF";
    public string SurfaceRaised { get; set; } = "#FFFFFF";
    public string SurfaceSunken { get; set; } = "#EDEAE3";

    // Brand.
    public string Primary { get; set; } = "#1F3A5F";
    public string OnPrimary { get; set; } = "#FFFFFF";
    public string Accent { get; set; } = "#8C6D3F";
    public string OnAccent { get; set; } = "#FFFFFF";

    // Text. Body must hit 4.5:1 on Surface; Secondary 4.5:1; Disabled is
    // exempt from contrast minimums but must remain visibly distinct.
    public string TextPrimary { get; set; } = "#1A1A1A";
    public string TextSecondary { get; set; } = "#4A4A4A";
    public string TextDisabled { get; set; } = "#9A9A9A";
    public string TextOnSurfaceInverse { get; set; } = "#FFFFFF";

    // Lines. Must stay visible in both light and dark themes.
    public string Border { get; set; } = "#D6D1C4";
    public string BorderStrong { get; set; } = "#B0A894";
    public string Divider { get; set; } = "#E3DFD5";

    // Status. Always paired with an icon or text in the UI: color is never
    // the sole carrier of meaning.
    public string Success { get; set; } = "#2E6B4F";
    public string Warning { get; set; } = "#8A6116";
    public string Error { get; set; } = "#A32222";
    public string Info { get; set; } = "#1F5673";

    // Interaction states.
    public string HoverOverlay { get; set; } = "#141A1F14";   // #RRGGBBAA
    public string PressedOverlay { get; set; } = "#141A1F29";
    public string SelectedOverlay { get; set; } = "#1F3A5F1F";
    public string FocusRing { get; set; } = "#2D6FB8";
    public string Scrim { get; set; } = "#00000099";          // ~60% black

    public ColorTokens Clone() => (ColorTokens)MemberwiseClone();
}

public sealed class TypographyTokens
{
    public string FontFamily { get; set; } = "Georgia, Cambria, serif";
    public string MonoFontFamily { get; set; } = "Consolas, monospace";

    /// <summary>Body size in DIP. All other sizes derive from this via the ramp.</summary>
    public double BaseSize { get; set; } = 14;

    /// <summary>Modular scale ratio used to build the size ramp.</summary>
    public double ScaleRatio { get; set; } = 1.25;

    public double LineHeightBody { get; set; } = 1.5;
    public double LineHeightHeading { get; set; } = 1.25;

    public int WeightRegular { get; set; } = 400;
    public int WeightMedium { get; set; } = 500;
    public int WeightBold { get; set; } = 700;

    // Ramp. Rounded to whole DIP to avoid subpixel text blur in WPF.
    [JsonIgnore] public double Caption => Math.Round(BaseSize / ScaleRatio);
    [JsonIgnore] public double Body => BaseSize;
    [JsonIgnore] public double Subtitle => Math.Round(BaseSize * ScaleRatio);
    [JsonIgnore] public double Title => Math.Round(BaseSize * Math.Pow(ScaleRatio, 2));
    [JsonIgnore] public double Headline => Math.Round(BaseSize * Math.Pow(ScaleRatio, 3));
    [JsonIgnore] public double Display => Math.Round(BaseSize * Math.Pow(ScaleRatio, 4));

    public TypographyTokens Clone() => (TypographyTokens)MemberwiseClone();
}

/// <summary>
/// 8px rhythm with a 4px half-step. Vertical section tiers are named
/// rather than ad-hoc so hierarchy stays consistent across tabs.
/// </summary>
public sealed class SpacingTokens
{
    public double Unit { get; set; } = 8;

    [JsonIgnore] public double Xxs => Unit * 0.5;  // 4
    [JsonIgnore] public double Xs => Unit;         // 8
    [JsonIgnore] public double Sm => Unit * 1.5;   // 12
    [JsonIgnore] public double Md => Unit * 2;     // 16
    [JsonIgnore] public double Lg => Unit * 3;     // 24
    [JsonIgnore] public double Xl => Unit * 4;     // 32
    [JsonIgnore] public double Xxl => Unit * 6;    // 48

    public SpacingTokens Clone() => (SpacingTokens)MemberwiseClone();
}

public sealed class ShapeTokens
{
    public double RadiusSm { get; set; } = 2;
    public double RadiusMd { get; set; } = 4;
    public double RadiusLg { get; set; } = 8;
    public double RadiusPill { get; set; } = 999;

    public double BorderWidth { get; set; } = 1;
    public double FocusRingWidth { get; set; } = 2;
    public double FocusRingOffset { get; set; } = 2;

    /// <summary>Elevation opacities for the shadow layers, lowest to highest.</summary>
    public double[] ElevationOpacity { get; set; } = { 0.06, 0.10, 0.16 };
    public double[] ElevationBlur { get; set; } = { 4, 10, 20 };

    public ShapeTokens Clone() => new()
    {
        RadiusSm = RadiusSm,
        RadiusMd = RadiusMd,
        RadiusLg = RadiusLg,
        RadiusPill = RadiusPill,
        BorderWidth = BorderWidth,
        FocusRingWidth = FocusRingWidth,
        FocusRingOffset = FocusRingOffset,
        ElevationOpacity = (double[])ElevationOpacity.Clone(),
        ElevationBlur = (double[])ElevationBlur.Clone(),
    };
}

/// <summary>
/// Durations in milliseconds, kept in the 150-300ms band. When the OS
/// reports reduced-motion, the shell scales these to zero rather than
/// branching every animation.
/// </summary>
public sealed class MotionTokens
{
    public double DurationFast { get; set; } = 120;
    public double DurationNormal { get; set; } = 200;
    public double DurationSlow { get; set; } = 300;

    public MotionTokens Clone() => (MotionTokens)MemberwiseClone();
}
