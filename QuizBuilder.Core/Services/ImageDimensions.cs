using System.Text;
namespace QuizBuilder.Core.Services;

/// <summary>
/// Reads pixel dimensions straight from image byte headers, for the handful of
/// formats the app accepts.
///
/// Why by hand: the Word export needs each image's width and height to size the
/// drawing, and Core is deliberately held to two package references. Pulling in
/// an imaging library for three header reads would not earn its keep. Each parse
/// is a few bytes at a fixed offset -- well-defined and testable.
/// </summary>
public static class ImageDimensions
{
    /// <summary>
    /// Width and height in pixels, or null if the bytes are not a recognised,
    /// well-formed image header. A null result means the caller should fall back
    /// to a default size rather than fail.
    /// </summary>
    public static (int Width, int Height)? Read(byte[]? bytes)
    {
        // Each format parser checks the bounds it needs. There is deliberately no
        // blanket minimum length here: PNG needs 24 bytes to reach the IHDR
        // height, but a GIF header is 10 and a small JPEG can be under 24, so a
        // shared floor would wrongly reject valid images of the other formats.
        if (bytes is null || bytes.Length < 10) return null;

        return ReadPng(bytes) ?? ReadGif(bytes) ?? ReadJpeg(bytes);
    }

    private static (int, int)? ReadPng(byte[] b)
    {
        // 8-byte signature, then the IHDR chunk: length(4) + "IHDR" + width(4) + height(4).
        if (b.Length < 24) return null;

        ReadOnlySpan<byte> sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        if (!b.AsSpan(0, 8).SequenceEqual(sig)) return null;
        if (b[12] != (byte)'I' || b[13] != (byte)'H' || b[14] != (byte)'D' || b[15] != (byte)'R') return null;

        var width = BigEndianInt(b, 16);
        var height = BigEndianInt(b, 20);
        return width > 0 && height > 0 ? (width, height) : null;
    }

    private static (int, int)? ReadGif(byte[] b)
    {
        // "GIF87a" or "GIF89a", then the logical screen descriptor: width(2 LE), height(2 LE).
        if (b.Length < 10) return null;

        var header = System.Text.Encoding.ASCII.GetString(b, 0, 6);
        if (header != "GIF87a" && header != "GIF89a") return null;

        var width = b[6] | (b[7] << 8);
        var height = b[8] | (b[9] << 8);
        return width > 0 && height > 0 ? (width, height) : null;
    }

    private static (int, int)? ReadJpeg(byte[] b)
    {
        // JPEG: starts FF D8, then a sequence of marker segments. The Start-Of-Frame
        // (SOFn) segment carries the dimensions: after its 2-byte length and a
        // 1-byte precision come height(2) then width(2), both big-endian.
        if (b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8) return null;

        var i = 2;
        while (i + 1 < b.Length)
        {
            if (b[i] != 0xFF) { i++; continue; }

            var marker = b[i + 1];
            i += 2;

            // Standalone markers (no length): padding, RST0-7, SOI/EOI.
            if (marker == 0xFF || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD9))
                continue;

            if (i + 1 >= b.Length) break;
            var segmentLength = (b[i] << 8) | b[i + 1];

            if (IsStartOfFrame(marker))
            {
                // length(2), precision(1), height(2), width(2)
                if (i + 6 >= b.Length) break;

                var height = (b[i + 3] << 8) | b[i + 4];
                var width = (b[i + 5] << 8) | b[i + 6];
                return width > 0 && height > 0 ? (width, height) : null;
            }

            if (segmentLength <= 0) break;
            i += segmentLength;
        }

        return null;
    }

    private static bool IsStartOfFrame(byte marker) => marker switch
    {
        // SOF0-3, 5-7, 9-11, 13-15 are frame headers; SOF4/8/12 (0xC4/0xC8/0xCC)
        // are not -- they are DHT / JPG / DAC.
        0xC0 or 0xC1 or 0xC2 or 0xC3 => true,
        0xC5 or 0xC6 or 0xC7 => true,
        0xC9 or 0xCA or 0xCB => true,
        0xCD or 0xCE or 0xCF => true,
        _ => false,
    };

    private static int BigEndianInt(byte[] b, int offset) =>
        (b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3];
}
