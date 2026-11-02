using PicKit.Core.Imaging;
using SkiaSharp;

namespace PicKit.Tests;

public class CropTests
{
    private const int Width = 1000;
    private const int Height = 800;

    private static readonly SKRect Middle = new(200, 200, 600, 500);

    [Fact]
    public void Crop_copies_exactly_the_requested_pixels()
    {
        using var source = TestImages.Numbered(10, 10);

        using var result = ImageOps.Crop(source, new SKRectI(2, 3, 5, 7));

        Assert.Equal((3, 4), (result.Width, result.Height));
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                Assert.Equal(source.Pixels[((y + 3) * 10) + x + 2], result.Pixels[(y * 3) + x]);
            }
        }
    }

    [Fact]
    public void Crop_outside_the_image_is_rejected()
    {
        using var source = TestImages.Numbered(10, 10);

        Assert.Throws<ArgumentOutOfRangeException>(() => ImageOps.Crop(source, new SKRectI(20, 20, 30, 30)));
    }

    [Theory]
    [InlineData(1f, 800, 800)]
    [InlineData(16f / 9f, 1000, 563)]
    [InlineData(9f / 16f, 450, 800)]
    public void Starting_box_is_largest_centred_box_of_that_shape(float aspect, int width, int height)
    {
        var box = CropGeometry.Initial(Width, Height, aspect);

        Assert.Equal(width, box.Width, 1f);
        Assert.Equal(height, box.Height, 1f);
        Assert.Equal(Width / 2f, box.MidX, 1f);
        Assert.Equal(Height / 2f, box.MidY, 1f);
    }

    [Fact]
    public void Free_crop_starts_at_the_whole_image()
    {
        Assert.Equal(new SKRect(0, 0, Width, Height), CropGeometry.Initial(Width, Height, null));
    }

    [Fact]
    public void Moving_stays_inside_the_image()
    {
        var box = CropGeometry.Drag(Middle, CropHandle.Move, new SKPoint(5000, -5000), null, Width, Height);

        Assert.Equal(new SKRect(600, 0, 1000, 300), box);
    }

    [Fact]
    public void Corner_drag_changes_only_that_corner()
    {
        var box = CropGeometry.Drag(Middle, CropHandle.BottomRight, new SKPoint(50, 20), null, Width, Height);

        Assert.Equal(new SKRect(200, 200, 650, 520), box);
    }

    [Theory]
    [InlineData(CropHandle.Left, -50, 0, 150, 200, 600, 500)]
    [InlineData(CropHandle.Right, 50, 0, 200, 200, 650, 500)]
    [InlineData(CropHandle.Top, 0, -50, 200, 150, 600, 500)]
    [InlineData(CropHandle.Bottom, 0, 50, 200, 200, 600, 550)]
    public void Edge_drag_moves_only_that_edge(CropHandle handle, float dx, float dy, float left, float top, float right, float bottom)
    {
        var box = CropGeometry.Drag(Middle, handle, new SKPoint(dx, dy), null, Width, Height);

        Assert.Equal(new SKRect(left, top, right, bottom), box);
    }

    [Fact]
    public void Dragging_past_the_opposite_edge_leaves_a_valid_box()
    {
        var box = CropGeometry.Drag(Middle, CropHandle.Left, new SKPoint(2000, 0), null, Width, Height);

        Assert.True(box.Width >= 1);
        Assert.Equal(Middle.Right, box.Right);
    }

    [Theory]
    [InlineData(CropHandle.TopLeft)]
    [InlineData(CropHandle.TopRight)]
    [InlineData(CropHandle.BottomLeft)]
    [InlineData(CropHandle.BottomRight)]
    [InlineData(CropHandle.Left)]
    [InlineData(CropHandle.Right)]
    [InlineData(CropHandle.Top)]
    [InlineData(CropHandle.Bottom)]
    public void Locked_aspect_survives_every_handle_and_stays_inside(CropHandle handle)
    {
        const float aspect = 16f / 9f;
        var start = CropGeometry.WithAspect(Middle, aspect, Width, Height);

        foreach (var delta in new[] { new SKPoint(90, 40), new SKPoint(-90, -40), new SKPoint(4000, 4000), new SKPoint(-4000, -4000) })
        {
            var box = CropGeometry.Drag(start, handle, delta, aspect, Width, Height);

            Assert.Equal(aspect, box.Width / box.Height, 2f);
            Assert.True(box.Left >= -0.5f && box.Top >= -0.5f, $"{handle} {delta}: {box}");
            Assert.True(box.Right <= Width + 0.5f && box.Bottom <= Height + 0.5f, $"{handle} {delta}: {box}");
        }
    }

    [Fact]
    public void Locked_corner_keeps_its_opposite_corner_pinned()
    {
        const float aspect = 1;
        var start = new SKRect(100, 100, 300, 300);

        var box = CropGeometry.Drag(start, CropHandle.BottomRight, new SKPoint(50, 10), aspect, Width, Height);

        Assert.Equal(100, box.Left);
        Assert.Equal(100, box.Top);
        Assert.Equal(250, box.Width);
    }

    [Fact]
    public void Changing_aspect_stays_centred_and_inside()
    {
        var box = CropGeometry.WithAspect(new SKRect(0, 0, 1000, 800), 1, Width, Height);

        Assert.Equal(new SKRect(100, 0, 900, 800), box);
    }

    [Fact]
    public void Pixel_snapping_stays_in_bounds_and_is_never_empty()
    {
        Assert.Equal(new SKRectI(0, 0, 1000, 800), CropGeometry.ToPixels(new SKRect(-5, -5, 2000, 2000), Width, Height));
        Assert.Equal(new SKRectI(999, 799, 1000, 800), CropGeometry.ToPixels(new SKRect(1200, 900, 1300, 950), Width, Height));

        var tiny = CropGeometry.ToPixels(new SKRect(10.2f, 10.2f, 10.3f, 10.3f), Width, Height);
        Assert.Equal((1, 1), (tiny.Width, tiny.Height));
    }

    [Theory]
    [InlineData(200, 200, CropHandle.TopLeft)]
    [InlineData(600, 500, CropHandle.BottomRight)]
    [InlineData(400, 200, CropHandle.Top)]
    [InlineData(200, 350, CropHandle.Left)]
    [InlineData(400, 350, CropHandle.Move)]
    [InlineData(100, 100, CropHandle.None)]
    public void Hit_testing_finds_the_right_handle(float x, float y, CropHandle expected)
    {
        Assert.Equal(expected, CropGeometry.HitTest(Middle, new SKPoint(x, y), 8));
    }
}
