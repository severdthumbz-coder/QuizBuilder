namespace QuizBuilder.Core.Theming;

/// <summary>
/// The five built-in themes. Every foreground/background pair used for text
/// was verified at >= 4.5:1, and every control boundary / focus ring at >= 3:1,
/// before being committed here. Changing a value in this file means re-checking
/// it -- the tests in ThemeContrastTests guard these ratios.
/// </summary>
public static class BuiltInThemes
{
    public const string AcademicId = "academic";
    public const string ModernMinimalId = "modern-minimal";
    public const string DarkExamId = "dark-exam";
    public const string PlayfulId = "playful";
    public const string CorporateId = "corporate";

    public static IReadOnlyList<ThemeTokens> All => new[]
    {
        Academic(), ModernMinimal(), DarkExam(), Playful(), Corporate()
    };

    public static ThemeTokens ById(string id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? Academic();

    /// <summary>Professional serif typography, muted ink-and-parchment palette.</summary>
    public static ThemeTokens Academic() => new()
    {
        Id = AcademicId,
        DisplayName = "Academic",
        IsDark = false,
        Colors = new ColorTokens
        {
            Background = "#F5F3EE",
            Surface = "#FFFFFF",
            SurfaceRaised = "#FFFFFF",
            SurfaceSunken = "#EDEAE3",
            Primary = "#1F3A5F",
            OnPrimary = "#FFFFFF",
            Accent = "#8C6D3F",
            OnAccent = "#FFFFFF",
            TextPrimary = "#1A1A1A",
            TextSecondary = "#4A4A4A",
            TextDisabled = "#9A9A9A",
            TextOnSurfaceInverse = "#FFFFFF",
            Border = "#D6D1C4",
            BorderStrong = "#6F6858",
            Divider = "#E3DFD5",
            Success = "#2E6B4F",
            Warning = "#8A6116",
            Error = "#A32222",
            Info = "#1F5673",
            HoverOverlay = "#1F3A5F14",
            PressedOverlay = "#1F3A5F29",
            SelectedOverlay = "#1F3A5F1F",
            FocusRing = "#2D6FB8",
            Scrim = "#00000099",
        },
        Typography = new TypographyTokens
        {
            FontFamily = "Georgia, Cambria, Times New Roman, serif",
            BaseSize = 14,
            ScaleRatio = 1.25,
        },
        Spacing = new SpacingTokens { Unit = 8 },
        Shape = new ShapeTokens { RadiusSm = 2, RadiusMd = 3, RadiusLg = 4 },
    };

    /// <summary>Clean sans-serif, generous whitespace, near-monochrome with one accent.</summary>
    public static ThemeTokens ModernMinimal() => new()
    {
        Id = ModernMinimalId,
        DisplayName = "Modern Minimal",
        IsDark = false,
        Colors = new ColorTokens
        {
            Background = "#FFFFFF",
            Surface = "#FFFFFF",
            SurfaceRaised = "#FFFFFF",
            SurfaceSunken = "#F4F5F7",
            Primary = "#111827",
            OnPrimary = "#FFFFFF",
            Accent = "#2563EB",
            OnAccent = "#FFFFFF",
            TextPrimary = "#111827",
            TextSecondary = "#4B5563",
            TextDisabled = "#9CA3AF",
            TextOnSurfaceInverse = "#FFFFFF",
            Border = "#E5E7EB",
            BorderStrong = "#6B7280",
            Divider = "#F3F4F6",
            Success = "#047857",
            Warning = "#B45309",
            Error = "#B91C1C",
            Info = "#1D4ED8",
            HoverOverlay = "#11182714",
            PressedOverlay = "#11182729",
            SelectedOverlay = "#2563EB14",
            FocusRing = "#2563EB",
            Scrim = "#00000099",
        },
        Typography = new TypographyTokens
        {
            FontFamily = "Segoe UI, Inter, system-ui, sans-serif",
            BaseSize = 14,
            ScaleRatio = 1.333,
        },
        Spacing = new SpacingTokens { Unit = 8 },
        Shape = new ShapeTokens { RadiusSm = 4, RadiusMd = 6, RadiusLg = 10 },
    };

    /// <summary>High-contrast dark mode intended for long invigilation sessions.</summary>
    public static ThemeTokens DarkExam() => new()
    {
        Id = DarkExamId,
        DisplayName = "Dark Exam",
        IsDark = true,
        Colors = new ColorTokens
        {
            Background = "#0E1116",
            Surface = "#161B22",
            SurfaceRaised = "#1C2129",
            SurfaceSunken = "#0B0E12",
            Primary = "#58A6FF",
            OnPrimary = "#0B0E12",
            Accent = "#F0B429",
            OnAccent = "#0B0E12",
            TextPrimary = "#E6EDF3",
            TextSecondary = "#A8B3BF",
            TextDisabled = "#6E7681",
            TextOnSurfaceInverse = "#0B0E12",
            Border = "#30363D",
            BorderStrong = "#6E7681",
            Divider = "#21262D",
            Success = "#3FB950",
            Warning = "#D29922",
            Error = "#F85149",
            Info = "#58A6FF",
            HoverOverlay = "#E6EDF314",
            PressedOverlay = "#E6EDF329",
            SelectedOverlay = "#58A6FF29",
            FocusRing = "#58A6FF",
            Scrim = "#000000B3",
        },
        Typography = new TypographyTokens
        {
            FontFamily = "Segoe UI, Inter, system-ui, sans-serif",
            BaseSize = 14,
            ScaleRatio = 1.25,
        },
        Spacing = new SpacingTokens { Unit = 8 },
        Shape = new ShapeTokens { RadiusSm = 3, RadiusMd = 5, RadiusLg = 8 },
    };

    /// <summary>Vibrant, warm, heavily rounded. Status colors are deliberately
    /// darker than the brand colors so they stay legible on white.</summary>
    public static ThemeTokens Playful() => new()
    {
        Id = PlayfulId,
        DisplayName = "Playful",
        IsDark = false,
        Colors = new ColorTokens
        {
            Background = "#FFF9F0",
            Surface = "#FFFFFF",
            SurfaceRaised = "#FFFFFF",
            SurfaceSunken = "#FFF1DC",
            Primary = "#C2255C",
            OnPrimary = "#FFFFFF",
            Accent = "#7048E8",
            OnAccent = "#FFFFFF",
            TextPrimary = "#2B2118",
            TextSecondary = "#5C4B3B",
            TextDisabled = "#A08B76",
            TextOnSurfaceInverse = "#FFFFFF",
            Border = "#F0D9BC",
            BorderStrong = "#8A6A45",
            Divider = "#F7E7D2",
            Success = "#237032",
            Warning = "#9C4409",
            Error = "#C92A2A",
            Info = "#1971C2",
            HoverOverlay = "#C2255C14",
            PressedOverlay = "#C2255C29",
            SelectedOverlay = "#7048E81F",
            FocusRing = "#7048E8",
            Scrim = "#00000099",
        },
        Typography = new TypographyTokens
        {
            FontFamily = "Verdana, Trebuchet MS, Segoe UI, sans-serif",
            BaseSize = 15,
            ScaleRatio = 1.25,
        },
        Spacing = new SpacingTokens { Unit = 8 },
        Shape = new ShapeTokens { RadiusSm = 8, RadiusMd = 12, RadiusLg = 18 },
    };

    /// <summary>Traditional sans-serif, restrained blues and teals.</summary>
    public static ThemeTokens Corporate() => new()
    {
        Id = CorporateId,
        DisplayName = "Corporate",
        IsDark = false,
        Colors = new ColorTokens
        {
            Background = "#F7F8FA",
            Surface = "#FFFFFF",
            SurfaceRaised = "#FFFFFF",
            SurfaceSunken = "#EDF0F4",
            Primary = "#0B4F8A",
            OnPrimary = "#FFFFFF",
            Accent = "#00707A",
            OnAccent = "#FFFFFF",
            TextPrimary = "#15202B",
            TextSecondary = "#44546A",
            TextDisabled = "#8C99A8",
            TextOnSurfaceInverse = "#FFFFFF",
            Border = "#D4DCE5",
            BorderStrong = "#5C6F85",
            Divider = "#E4E9EF",
            Success = "#1E6B41",
            Warning = "#8A5A00",
            Error = "#A4262C",
            Info = "#0B4F8A",
            HoverOverlay = "#0B4F8A14",
            PressedOverlay = "#0B4F8A29",
            SelectedOverlay = "#0B4F8A1F",
            FocusRing = "#0B4F8A",
            Scrim = "#00000099",
        },
        Typography = new TypographyTokens
        {
            FontFamily = "Segoe UI, Tahoma, Arial, sans-serif",
            BaseSize = 14,
            ScaleRatio = 1.2,
        },
        Spacing = new SpacingTokens { Unit = 8 },
        Shape = new ShapeTokens { RadiusSm = 2, RadiusMd = 4, RadiusLg = 6 },
    };
}
