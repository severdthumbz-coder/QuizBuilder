using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Reading pixel dimensions from image byte headers. The Word export sizes each
/// drawing from these, so a wrong read means a wrongly-sized picture.
/// </summary>
public class ImageDimensionsTests
{
    private static byte[] Png(int w, int h)
    {
        var sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        using var ms = new System.IO.MemoryStream();
        ms.Write(sig);

        // IHDR chunk: length(4)=13, "IHDR", width(4), height(4), + 5 bytes, CRC(4).
        void BeInt(int v) => ms.Write(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });
        BeInt(13);
        ms.Write(new[] { (byte)'I', (byte)'H', (byte)'D', (byte)'R' });
        BeInt(w);
        BeInt(h);
        ms.Write(new byte[] { 0x08, 0x02, 0x00, 0x00, 0x00 });
        BeInt(0); // CRC placeholder; the reader does not check it

        return ms.ToArray();
    }

    private static byte[] Gif(int w, int h) =>
        new byte[] { (byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a',
            (byte)(w & 0xFF), (byte)(w >> 8), (byte)(h & 0xFF), (byte)(h >> 8),
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    private static byte[] Jpeg(int w, int h) =>
        new byte[]
        {
            0xFF, 0xD8,                 // SOI
            0xFF, 0xC0,                 // SOF0
            0x00, 0x11,                 // length 17
            0x08,                       // precision
            (byte)(h >> 8), (byte)h,    // height
            (byte)(w >> 8), (byte)w,    // width
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0xFF, 0xD9,                 // EOI
        };

    [Fact]
    public void ReadsPngDimensions()
    {
        Assert.Equal((120, 80), ImageDimensions.Read(Png(120, 80)));
    }

    [Fact]
    public void ReadsGifDimensions()
    {
        Assert.Equal((64, 48), ImageDimensions.Read(Gif(64, 48)));
    }

    [Fact]
    public void ReadsJpegDimensions()
    {
        Assert.Equal((200, 150), ImageDimensions.Read(Jpeg(200, 150)));
    }

    [Fact]
    public void ReturnsNullForUnrecognisedBytes()
    {
        Assert.Null(ImageDimensions.Read(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24 }));
    }

    [Fact]
    public void ReturnsNullForNullOrTiny()
    {
        Assert.Null(ImageDimensions.Read(null));
        Assert.Null(ImageDimensions.Read(new byte[] { 1, 2, 3 }));
    }
}
