using System.Globalization;

namespace QuizBuilder.Player.Services;

/// <summary>True when a string is non-empty; used to show/hide optional rows.</summary>
public sealed class NotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && !string.IsNullOrWhiteSpace(s);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Boolean negation, for IsEnabled/IsVisible bound to a busy flag.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}

/// <summary>True when a byte[] is present and non-empty; used to show/hide an
/// optional image whose bytes are resolved from the package. NotEmptyConverter
/// only understands strings, so image visibility needs its own test.</summary>
public sealed class HasBytesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is byte[] { Length: > 0 };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
