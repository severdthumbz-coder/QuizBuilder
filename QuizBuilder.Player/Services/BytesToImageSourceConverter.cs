using System.Globalization;

namespace QuizBuilder.Player.Services;

/// <summary>
/// Turns raw image bytes (as loaded from a .qbx by Core's package service) into
/// a MAUI ImageSource. The desktop app has its own WPF BytesToImageConverter;
/// this is the small MAUI-specific equivalent the HANDOFF anticipated -- Core
/// stays imaging-free and each host supplies its own converter.
///
/// A fresh MemoryStream is handed to ImageSource.FromStream on every call
/// because ImageSource may read the stream lazily and more than once; sharing
/// one stream across bindings leads to a disposed/again-read stream.
/// </summary>
public sealed class BytesToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] { Length: > 0 } bytes) return null;

        // Copy the bytes into the closure so each stream is independent.
        var snapshot = bytes;
        return ImageSource.FromStream(() => new MemoryStream(snapshot));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
