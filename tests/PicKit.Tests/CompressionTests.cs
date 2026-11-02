using PicKit.Core.Imaging;
using SkiaSharp;

namespace PicKit.Tests;

public class CompressionTests
{
    [Theory]
    [InlineData(ImageFormat.Jpeg)]
    [InlineData(ImageFormat.Png)]
    [InlineData(ImageFormat.WebP)]
    public void Output_decodes_back_to_a_valid_image_of_that_format(ImageFormat format)
    {
        using var source = TestImages.Photo(320, 200);

        var data = ImageEncoder.Encode(source, new EncodeOptions(format, 80));

        using var decoded = ImageDecoder.Decode(data);
        Assert.Equal(format, decoded.Format);
        Assert.Equal((320, 200), (decoded.Pixels.Width, decoded.Pixels.Height));
    }

    [Theory]
    [InlineData(ImageFormat.Jpeg)]
    [InlineData(ImageFormat.WebP)]
    public void Lower_presets_make_smaller_files(ImageFormat format)
    {
        using var source = TestImages.Photo(800, 600);

        long SizeOf(CompressionPreset preset) =>
            ImageEncoder.Encode(source, new EncodeOptions(format, Compression.QualityFor(format, preset))).Length;

        var high = SizeOf(CompressionPreset.High);
        var balanced = SizeOf(CompressionPreset.Balanced);
        var small = SizeOf(CompressionPreset.Small);

        Assert.True(high > balanced && balanced > small, $"{high} > {balanced} > {small}");
    }

    [Theory]
    [InlineData(ImageFormat.Jpeg, 40_000)]
    [InlineData(ImageFormat.WebP, 40_000)]
    [InlineData(ImageFormat.Png, 200_000)]
    public void Target_size_lands_under_the_limit_and_reports_the_real_result(ImageFormat format, long target)
    {
        using var source = TestImages.Photo(1000, 750);

        var result = Compression.ToTargetSize(source, format, target);

        Assert.True(result.MetTarget);
        Assert.True(result.Size <= target, $"{result.Size} over {target}");
        Assert.Equal(result.Data.LongLength, result.Size);

        using var decoded = ImageDecoder.Decode(result.Data);
        Assert.Equal((result.Width, result.Height), (decoded.Pixels.Width, decoded.Pixels.Height));
    }

    [Fact]
    public void Target_size_prefers_lowering_quality_to_shrinking_the_image()
    {
        using var source = TestImages.Photo(1000, 750);
        var full = ImageEncoder.Encode(source, new EncodeOptions(ImageFormat.Jpeg, 90));

        var result = Compression.ToTargetSize(source, ImageFormat.Jpeg, full.Length * 3 / 4);

        Assert.True(result.MetTarget);
        Assert.Equal(1000, result.Width);
    }

    [Fact]
    public void Target_size_uses_most_of_the_allowance()
    {
        using var source = TestImages.Photo(1000, 750);
        const long target = 100_000;

        var result = Compression.ToTargetSize(source, ImageFormat.Jpeg, target);

        Assert.True(result.Size > target * 0.7, $"Only used {result.Size} of {target}");
    }

    [Fact]
    public void Target_size_shrinks_when_quality_alone_cannot_get_there()
    {
        using var source = TestImages.Photo(1000, 750);

        var result = Compression.ToTargetSize(source, ImageFormat.Jpeg, 8_000);

        Assert.True(result.MetTarget);
        Assert.True(result.Width < 1000);
        Assert.Equal(1000f / 750f, (float)result.Width / result.Height, 1f);
    }

    [Fact]
    public void Impossible_target_says_so_instead_of_pretending()
    {
        using var source = TestImages.Photo(100, 80);

        var result = Compression.ToTargetSize(source, ImageFormat.Png, 16);

        Assert.False(result.MetTarget);
        Assert.True(result.Size > 16);
    }

    [Fact]
    public void Target_size_can_be_cancelled()
    {
        using var source = TestImages.Photo(400, 300);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            Compression.ToTargetSize(source, ImageFormat.Jpeg, 1000, cancellationToken: cancelled.Token));
    }

    [Fact]
    public void Target_size_does_not_alter_the_source()
    {
        using var source = TestImages.Photo(300, 200);
        using var before = source.Clone();

        Compression.ToTargetSize(source, ImageFormat.Jpeg, 2_000);

        Assert.True(before.Pixels.SequenceEqual(source.Pixels));
    }

    [Fact]
    public void Png_to_jpeg_to_png_conversions_work()
    {
        using var source = TestImages.Photo(200, 150);

        var jpeg = ImageEncoder.Encode(source, new EncodeOptions(ImageFormat.Jpeg, 92));
        using var fromJpeg = ImageDecoder.Decode(jpeg);
        var png = ImageEncoder.Encode(fromJpeg.Pixels, new EncodeOptions(ImageFormat.Png));
        using var fromPng = ImageDecoder.Decode(png);

        Assert.Equal(ImageFormat.Png, fromPng.Format);
        Assert.True(fromJpeg.Pixels.Pixels.SequenceEqual(fromPng.Pixels.Pixels));
    }

    [Fact]
    public void Png_is_exact()
    {
        using var source = TestImages.Numbered(100, 100);

        var png = ImageEncoder.Encode(source, new EncodeOptions(ImageFormat.Png));
        using var decoded = ImageDecoder.Decode(png);

        Assert.True(source.Pixels.SequenceEqual(decoded.Pixels.Pixels));
    }

    [Fact]
    public void WebP_keeps_transparency()
    {
        using var source = TestImages.Solid(64, 64, SKColors.Transparent);
        using (var canvas = source.CreateCanvas())
        using (var paint = new SKPaint { Color = SKColors.Red })
        {
            canvas.DrawRect(16, 16, 32, 32, paint);
        }

        var webp = ImageEncoder.Encode(source, new EncodeOptions(ImageFormat.WebP, 90));
        using var decoded = ImageDecoder.Decode(webp);

        Assert.True(decoded.Pixels.HasTransparency());
        Assert.Equal(0u, decoded.Pixels.Pixels[0] >> 24);
    }

    [Theory]
    [InlineData(0xFFFFFFFF, 255)]
    [InlineData(0xFF000000, 0)]
    public void Jpeg_fills_transparency_with_the_chosen_colour(uint background, int expectedChannel)
    {
        using var source = TestImages.Solid(64, 64, SKColors.Transparent);

        var jpeg = ImageEncoder.Encode(source, new EncodeOptions(ImageFormat.Jpeg, 95, new SKColor(background)));
        using var decoded = ImageDecoder.Decode(jpeg);

        Assert.InRange((int)(decoded.Pixels.Pixels[(32 * 64) + 32] & 0xFF), expectedChannel - 2, expectedChannel + 2);
        Assert.False(decoded.Pixels.HasTransparency());
    }

    [Fact]
    public void Jpeg_with_transparency_requires_an_explicit_decision()
    {
        using var source = TestImages.Solid(16, 16, SKColors.Transparent);

        Assert.Throws<ArgumentException>(() => ImageEncoder.Encode(source, new EncodeOptions(ImageFormat.Jpeg)));
    }

    [Fact]
    public void Opaque_images_need_no_background_for_jpeg()
    {
        using var source = TestImages.Photo(16, 16);

        Assert.NotEmpty(ImageEncoder.Encode(source, new EncodeOptions(ImageFormat.Jpeg)));
    }

    [Theory]
    [InlineData(ImageFormat.Gif)]
    [InlineData(ImageFormat.Tiff)]
    public void Unsupported_output_formats_are_refused_clearly(ImageFormat format)
    {
        using var source = TestImages.Photo(16, 16);

        var error = Assert.Throws<ImageException>(() => ImageEncoder.Encode(source, new EncodeOptions(format)));

        Assert.Contains("can't write", error.Message);
    }
}
