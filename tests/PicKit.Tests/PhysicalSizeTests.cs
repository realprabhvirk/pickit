using System.Globalization;
using PicKit.Core.Imaging;

namespace PicKit.Tests;

public class PhysicalSizeTests
{
    [Theory]
    [InlineData(25.4, SizeUnit.Millimetres, 300, 300)]
    [InlineData(2.54, SizeUnit.Centimetres, 300, 300)]
    [InlineData(100, SizeUnit.Millimetres, 300, 1181)]
    [InlineData(10, SizeUnit.Centimetres, 96, 378)]
    [InlineData(640, SizeUnit.Pixels, 300, 640)]
    public void Sizes_convert_to_pixels(double value, SizeUnit unit, int dpi, int expected) =>
        Assert.Equal(expected, PhysicalSize.ToPixels(value, unit, dpi));

    [Theory]
    [InlineData(300, SizeUnit.Millimetres, 300, 25.4)]
    [InlineData(300, SizeUnit.Centimetres, 300, 2.54)]
    [InlineData(640, SizeUnit.Pixels, 300, 640)]
    public void Pixels_convert_to_sizes(int pixels, SizeUnit unit, int dpi, double expected) =>
        Assert.Equal(expected, PhysicalSize.FromPixels(pixels, unit, dpi), 6);

    [Theory]
    [InlineData(SizeUnit.Millimetres)]
    [InlineData(SizeUnit.Centimetres)]
    public void A_displayed_size_reads_back_as_the_same_pixels(SizeUnit unit)
    {
        foreach (var dpi in new[] { 72, 96, 150, 300, 600 })
        {
            for (var pixels = 1; pixels < 12_000; pixels += 7)
            {
                var shown = PhysicalSize.Format(PhysicalSize.FromPixels(pixels, unit, dpi), unit);

                Assert.True(PhysicalSize.TryParse(shown, out var parsed));
                Assert.Equal(pixels, PhysicalSize.ToPixels(parsed, unit, dpi));
            }
        }
    }

    [Theory]
    [InlineData("12.5", 12.5)]
    [InlineData("100", 100)]
    public void Numbers_parse_with_a_dot(string text, double expected)
    {
        Assert.True(PhysicalSize.TryParse(text, out var value));
        Assert.Equal(expected, value);
    }

    [Fact]
    public void A_comma_decimal_is_understood_where_the_pc_uses_one()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            Assert.True(PhysicalSize.TryParse("12,5", out var value));
            Assert.Equal(12.5, value);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("banana")]
    [InlineData("")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Nonsense_is_rejected(string text) => Assert.False(PhysicalSize.TryParse(text, out _));

    [Fact]
    public void A_huge_size_does_not_overflow() =>
        Assert.Equal(int.MaxValue, PhysicalSize.ToPixels(1e300, SizeUnit.Centimetres, PhysicalSize.MaxDpi));

    [Fact]
    public void Stamping_a_jpeg_keeps_it_readable_and_the_picture_the_same()
    {
        using var image = TestImages.Photo(64, 48);
        var original = ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Jpeg));

        var stamped = ImageResolution.Stamp(original, ImageFormat.Jpeg, 300);

        using var before = ImageDecoder.Decode(original);
        using var after = ImageDecoder.Decode(stamped);
        Assert.True(before.Pixels.Pixels.SequenceEqual(after.Pixels.Pixels));
    }

    [Fact]
    public void Stamping_a_png_keeps_it_readable_and_the_picture_the_same()
    {
        using var image = TestImages.Photo(64, 48);
        var original = ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png));

        var stamped = ImageResolution.Stamp(original, ImageFormat.Png, 300);

        using var before = ImageDecoder.Decode(original);
        using var after = ImageDecoder.Decode(stamped);
        Assert.True(before.Pixels.Pixels.SequenceEqual(after.Pixels.Pixels));
    }

    [Fact]
    public void A_jpeg_without_a_header_gets_one()
    {
        byte[] bare = [0xFF, 0xD8, 0xFF, 0xDB, 0, 2, 0xFF, 0xD9];

        var stamped = ImageResolution.Stamp(bare, ImageFormat.Jpeg, 300);

        Assert.Equal(bare.Length + 18, stamped.Length);
        Assert.Equal(0xE0, stamped[3]);
        Assert.Equal(1, stamped[13]);
        Assert.Equal(300, (stamped[14] << 8) | stamped[15]);
        Assert.Equal(0xDB, stamped[21]);
    }

    [Fact]
    public void Webp_is_left_alone_because_it_has_no_field_for_this()
    {
        byte[] data = [1, 2, 3];

        Assert.False(ImageResolution.CanStore(ImageFormat.WebP));
        Assert.Same(data, ImageResolution.Stamp(data, ImageFormat.WebP, 300));
    }
}
