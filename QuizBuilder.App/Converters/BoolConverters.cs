using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using QuizBuilder.App.Theming;

namespace QuizBuilder.App.Converters;

/// <summary>
/// True -> Visible, False -> Collapsed. Set Invert to flip.
///
/// Collapsed rather than Hidden: Hidden reserves layout space, which leaves
/// gaps where an item was meant to disappear entirely.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("One-way only.");
}

/// <summary>
/// Picks one of two strings by a boolean. Used for the feature list's status
/// glyph, so the marker is not carried by colour alone: a tick and a dash
/// differ in shape as well as hue, which matters for colour-blind users and in
/// greyscale print.
/// </summary>
public sealed class BoolToStringConverter : IValueConverter
{
    public string TrueValue { get; set; } = string.Empty;
    public string FalseValue { get; set; } = string.Empty;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? TrueValue : FalseValue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("One-way only.");
}

/// <summary>
/// Picks one of two brushes by a boolean, resolved from the app's theme
/// resources at convert time rather than baked in. Used to tint the feature
/// status glyph without hardcoding a colour that would ignore the theme.
/// </summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public string TrueResourceKey { get; set; } = "Brush.Success";
    public string FalseResourceKey { get; set; } = "Brush.TextDisabled";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is true ? TrueResourceKey : FalseResourceKey;

        // TryFindResource on Application walks the merged theme dictionary,
        // so this follows a live theme switch.
        return Application.Current?.TryFindResource(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("One-way only.");
}

/// <summary>
/// Hex token string -> Brush.
///
/// Needed because the ViewModels expose colours as hex strings (keeping Core
/// and the ViewModels free of WPF types), while the swatch UI needs Brushes.
/// Parsing goes through ThemeResourceBuilder.ParseColor so the token's
/// CSS-order alpha (#RRGGBBAA) is read the same way everywhere -- WPF's own
/// converter expects #AARRGGBB and would silently misread every overlay.
/// </summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return Brushes.Transparent;

        try
        {
            var brush = new SolidColorBrush(ThemeResourceBuilder.ParseColor(hex));
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            // A malformed token must not crash the picker. Magenta is
            // deliberately loud: a silent fallback would hide the bad value.
            return Brushes.Magenta;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("One-way only.");
}
