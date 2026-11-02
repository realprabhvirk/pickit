using PicKit.Core.Imaging;
using SkiaSharp;

namespace PicKit.Tests;

public class DecoderTests
{
    [Fact]
    public void Round_trips_a_png_exactly()
    {
        using var source = TestImages.Numbered(20, 10);
        using var data = source.Bitmap.PeekPixels().Encode(new SKPngEncoderOptions())!;

        using var decoded = ImageDecoder.Decode(data.ToArray());

        Assert.Equal(ImageFormat.Png, decoded.Format);
        Assert.True(source.Pixels.SequenceEqual(decoded.Pixels.Pixels));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(64)]
    public void Garbage_throws_a_friendly_error_rather_than_crashing(int length)
    {
        var bytes = new byte[length];
        new Random(1).NextBytes(bytes);

        var error = Assert.Throws<ImageException>(() => ImageDecoder.Decode(bytes));

        Assert.StartsWith("PicKit couldn't open this image.", error.Message);
    }

    [Fact]
    public void Truncated_jpeg_reports_incomplete_rather_than_failing()
    {
        using var source = TestImages.Photo(400, 300);
        using var data = source.Bitmap.PeekPixels().Encode(new SKJpegEncoderOptions(90))!;
        var cut = data.ToArray().AsSpan(0, (int)(data.Size * 0.6)).ToArray();

        using var decoded = ImageDecoder.Decode(cut);

        Assert.True(decoded.IsIncomplete);
        Assert.Equal(400, decoded.Pixels.Width);
    }

    [Fact]
    public void Header_claiming_absurd_dimensions_is_refused_before_allocating()
    {
        // A valid PNG header announcing 60000 x 60000: the classic decompression bomb.
        byte[] bomb =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0xEA, 0x60, 0x00, 0x00, 0xEA, 0x60,
            0x08, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        ];

        Assert.Throws<ImageException>(() => ImageDecoder.Decode(bomb));
    }

    [Fact]
    public void Missing_file_gives_a_friendly_error()
    {
        var error = Assert.Throws<ImageException>(() => ImageDecoder.Decode(@"C:\does\not\exist.png"));

        Assert.Equal(ImageErrorKind.Unreadable, error.Kind);
    }
}
