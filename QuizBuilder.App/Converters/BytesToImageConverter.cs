using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace QuizBuilder.App.Converters;

/// <summary>
/// Turns image bytes into an ImageSource for an Image control.
///
/// The bytes are decoded eagerly (OnLoad) and the stream closed, so the source
/// does not keep a file or buffer locked and the control can render detached
/// from the original array. Returns null for no bytes, which simply shows
/// nothing.
/// </summary>
public sealed class BytesToImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not byte[] bytes || bytes.Length == 0) return null;

        try
        {
            using var stream = new MemoryStream(bytes);

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;   // decode now, then release the stream
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();                                  // usable across threads, and immutable

            return image;
        }
        catch
        {
            // A corrupt or unsupported image should not crash the editor; show
            // nothing, exactly as if there were no image.
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
