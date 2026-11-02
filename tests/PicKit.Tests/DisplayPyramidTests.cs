using PicKit.Core.Imaging;
using PicKit.Core.Rendering;
using SkiaSharp;

namespace PicKit.Tests;

public class DisplayPyramidTests
{
    [Fact]
    public void Full_size_is_used_at_and_above_100_percent()
    {
        using var source = TestImages.Photo(1024, 768);
        using var pyramid = new DisplayPyramid(source);

        Assert.Same(source, pyramid.ForZoom(1));
        Assert.Same(source, pyramid.ForZoom(0.6));
        Assert.Equal(0, pyramid.BuiltLevels);
    }

    [Theory]
    [InlineData(0.5, 512)]
    [InlineData(0.25, 256)]
    public void Level_never_has_fewer_pixels_than_the_screen_needs(double zoom, int expectedWidth)
    {
        using var source = TestImages.Photo(1024, 768);
        using var pyramid = new DisplayPyramid(source);

        var level = pyramid.ForZoom(zoom);

        Assert.Equal(expectedWidth, level.Width);
        Assert.True(level.Width >= source.Width * zoom);
    }

    [Fact]
    public void Tiny_zoom_stops_at_a_usable_size_on_small_images()
    {
        using var source = TestImages.Photo(200, 100);
        using var pyramid = new DisplayPyramid(source);

        Assert.True(pyramid.ForZoom(0.01).Width >= 100);
    }

    [Fact]
    public void Halving_averages_correctly()
    {
        using var source = ImageBuffer.Create(2, 2);
        source.Pixels[0] = 0xFF000000;
        source.Pixels[1] = 0xFFFFFFFF;
        source.Pixels[2] = 0xFF000000;
        source.Pixels[3] = 0xFFFFFFFF;

        using var half = PixelOps.Halve(source);

        Assert.Equal(1, half.Width);
        Assert.Equal(0xFF808080, half.Pixels[0]);
    }

    [Fact]
    public void Halving_an_opaque_image_stays_opaque()
    {
        using var source = TestImages.Photo(301, 203);

        using var half = PixelOps.Halve(source);

        Assert.False(half.HasTransparency());
    }

    [Fact]
    public void Region_update_matches_a_complete_rebuild()
    {
        using var source = TestImages.Photo(800, 600);
        using var pyramid = new DisplayPyramid(source);
        var stale = pyramid.ForZoom(0.2);

        var region = new SKRectI(101, 77, 333, 299);
        using (var canvas = source.CreateCanvas())
        using (var paint = new SKPaint { Color = SKColors.Magenta })
        {
            canvas.DrawRect(region, paint);
        }

        pyramid.Update(region);

        using var fresh = new DisplayPyramid(source);
        var expected = fresh.ForZoom(0.2);
        Assert.True(expected.Pixels.SequenceEqual(stale.Pixels));
    }
}
