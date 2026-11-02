using System.Collections.Immutable;
using PicKit.Core.Documents;
using PicKit.Core.Imaging;
using PicKit.Core.Markup;
using SkiaSharp;

namespace PicKit.Tests;

public class MarkupTests
{
    private static readonly MarkupStyle Red = new(SKColors.Red, 10);

    private static StrokeElement Stroke(params SKPoint[] points) => new()
    {
        Style = Red,
        Points = [.. points],
    };

    private static uint PixelAt(ImageBuffer image, int x, int y) => image.Pixels[(y * image.Width) + x];

    private static int ChannelDifference(uint a, uint b)
    {
        var largest = 0;
        for (var shift = 0; shift < 32; shift += 8)
        {
            largest = Math.Max(largest, Math.Abs((int)((a >> shift) & 0xFF) - (int)((b >> shift) & 0xFF)));
        }

        return largest;
    }

    private static ImageBuffer Render(MarkupElement element, int size = 200, SKColor? background = null)
    {
        var image = TestImages.Solid(size, size, background ?? SKColors.White);
        using var canvas = image.CreateCanvas();
        element.Draw(canvas);
        return image;
    }

    [Fact]
    public void Pen_stroke_paints_along_its_path()
    {
        using var image = Render(Stroke(new SKPoint(20, 100), new SKPoint(180, 100)));

        Assert.Equal(0xFFFF0000, PixelAt(image, 100, 100));
        Assert.Equal(0xFFFFFFFF, PixelAt(image, 100, 20));
    }

    [Fact]
    public void Single_click_leaves_a_dot()
    {
        using var image = Render(Stroke(new SKPoint(100, 100)));

        Assert.Equal(0xFFFF0000, PixelAt(image, 100, 100));
    }

    [Fact]
    public void Highlighter_tints_without_hiding_dark_content()
    {
        var yellow = new MarkupStyle(new SKColor(0xFF, 0xE0, 0x00), 40, 1);
        var element = new StrokeElement
        {
            Style = yellow,
            IsHighlighter = true,
            Points = [new SKPoint(0, 100), new SKPoint(200, 100)],
        };

        using var onWhite = Render(element, background: SKColors.White);
        using var onBlack = Render(element, background: SKColors.Black);

        Assert.Equal(0xFFFFE000, PixelAt(onWhite, 100, 100));
        Assert.Equal(0xFF000000, PixelAt(onBlack, 100, 100));
    }

    [Fact]
    public void Self_crossing_highlight_has_one_even_tint()
    {
        var style = new MarkupStyle(SKColors.Yellow, 30, 0.5f);
        var element = new StrokeElement
        {
            Style = style,
            IsHighlighter = true,
            Points = [new SKPoint(20, 100), new SKPoint(180, 100), new SKPoint(180, 120), new SKPoint(100, 120), new SKPoint(100, 20)],
        };

        using var image = Render(element);

        Assert.Equal(PixelAt(image, 60, 100), PixelAt(image, 100, 105));
    }

    [Fact]
    public void Opacity_blends_with_the_image()
    {
        var element = Stroke(new SKPoint(0, 100), new SKPoint(200, 100)) with { Style = new MarkupStyle(SKColors.Black, 20, 0.5f) };

        using var image = Render(element);

        var channel = PixelAt(image, 100, 100) & 0xFF;
        Assert.InRange(channel, 120u, 136u);
    }

    [Fact]
    public void Arrow_draws_shaft_and_a_wider_head()
    {
        var element = new LineElement { Style = Red, Start = new SKPoint(20, 100), End = new SKPoint(180, 100), HasArrowhead = true };

        using var image = Render(element);

        Assert.Equal(0xFFFF0000, PixelAt(image, 60, 100));
        Assert.Equal(0xFFFFFFFF, PixelAt(image, 60, 112));
        Assert.Equal(0xFFFF0000, PixelAt(image, 150, 110));
    }

    [Theory]
    [InlineData(ShapeKind.Rectangle, 50, 100, true)]
    [InlineData(ShapeKind.Rectangle, 100, 100, false)]
    [InlineData(ShapeKind.Ellipse, 50, 100, true)]
    [InlineData(ShapeKind.Ellipse, 100, 100, false)]
    public void Shapes_are_outlines(ShapeKind kind, int x, int y, bool painted)
    {
        var element = new ShapeElement { Style = Red, Kind = kind, Box = new SKRect(50, 50, 150, 150) };

        using var image = Render(element);

        Assert.Equal(painted ? 0xFFFF0000 : 0xFFFFFFFF, PixelAt(image, x, y));
    }

    [Fact]
    public void Text_renders_inside_its_measured_bounds()
    {
        var element = new TextElement { Style = Red, Position = new SKPoint(20, 40), Text = "Hello", FontSize = 40 };

        using var image = Render(element);

        var bounds = element.Bounds;
        Assert.True(bounds.Width > 40);
        var painted = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (PixelAt(image, x, y) != 0xFFFFFFFF)
                {
                    painted++;
                    Assert.True(SKRect.Inflate(bounds, 3, 3).Contains(x, y), $"Ink at {x},{y} is outside {bounds}");
                }
            }
        }

        Assert.True(painted > 100);
    }

    [Fact]
    public void Multi_line_text_is_taller()
    {
        var one = new TextElement { Style = Red, Position = new SKPoint(0, 0), Text = "A", FontSize = 20 };
        var two = one with { Text = "A\nB" };

        Assert.Equal(one.Bounds.Height * 2, two.Bounds.Height, 1f);
    }

    [Fact]
    public void Hit_testing_follows_each_shape()
    {
        var stroke = Stroke(new SKPoint(0, 0), new SKPoint(100, 0));
        Assert.True(stroke.HitTest(new SKPoint(50, 4), 0));
        Assert.False(stroke.HitTest(new SKPoint(50, 30), 0));

        var hollow = new ShapeElement { Style = Red, Kind = ShapeKind.Rectangle, Box = new SKRect(0, 0, 100, 100) };
        Assert.True(hollow.HitTest(new SKPoint(0, 50), 2));
        Assert.False(hollow.HitTest(new SKPoint(50, 50), 2));

        var ring = new ShapeElement { Style = Red, Kind = ShapeKind.Ellipse, Box = new SKRect(0, 0, 100, 100) };
        Assert.True(ring.HitTest(new SKPoint(100, 50), 2));
        Assert.False(ring.HitTest(new SKPoint(50, 50), 2));
    }

    [Fact]
    public void Rotating_the_image_carries_markup_with_its_pixels()
    {
        const int width = 300;
        const int height = 200;
        var element = new ShapeElement { Style = Red, Kind = ShapeKind.Rectangle, Box = new SKRect(40, 50, 100, 90) };

        using var document = new EditorDocument(TestImages.Solid(width, height, SKColors.White), new DocumentSource(null, ImageFormat.Png, 0));
        document.SetMarkups("Add", [element]);
        using var before = document.RenderFlattened();
        using var expected = OrthoTransforms.Apply(before, OrthoTransform.Rotate90);

        document.ReplacePixels(
            "Rotate",
            OrthoTransforms.Apply(document.Pixels, OrthoTransform.Rotate90),
            OrthoTransforms.ToMatrix(OrthoTransform.Rotate90, width, height));

        // Skia's rounded joins rasterise a little differently once turned, so only their edge
        // pixels may differ, and never by much. A misplaced shape would differ by 255 along its length.
        using var after = document.RenderFlattened();
        Assert.Equal((expected.Width, expected.Height), (after.Width, after.Height));

        var worst = 0;
        var differing = 0;
        for (var i = 0; i < expected.Pixels.Length; i++)
        {
            var difference = ChannelDifference(expected.Pixels[i], after.Pixels[i]);
            worst = Math.Max(worst, difference);
            differing += difference > 0 ? 1 : 0;
        }

        Assert.True(worst <= 64 && differing <= 32, $"worst {worst}, differing pixels {differing}");
    }

    [Fact]
    public void Scaling_scales_thickness_and_font()
    {
        var matrix = SKMatrix.CreateScale(0.5f, 0.5f);

        var line = (LineElement)new LineElement { Style = Red, Start = new SKPoint(100, 100), End = new SKPoint(200, 100) }.Transformed(matrix);
        var text = (TextElement)new TextElement { Style = Red, Position = new SKPoint(100, 100), Text = "A", FontSize = 40 }.Transformed(matrix);

        Assert.Equal(5, line.Style.Thickness);
        Assert.Equal(new SKPoint(50, 50), line.Start);
        Assert.Equal(20, text.FontSize);
    }

    [Fact]
    public void Rotating_turns_text_with_the_image()
    {
        var text = new TextElement { Style = Red, Position = new SKPoint(10, 20), Text = "A", FontSize = 20 };

        var turned = (TextElement)text.Transformed(OrthoTransforms.ToMatrix(OrthoTransform.Rotate90, 300, 200));

        Assert.Equal(90, turned.Rotation, 2f);
        Assert.Equal(new SKPoint(180, 10), turned.Position);
    }

    [Fact]
    public void Flipping_keeps_text_readable()
    {
        var text = new TextElement { Style = Red, Position = new SKPoint(10, 20), Text = "ABC", FontSize = 20 };

        var flipped = (TextElement)text.Transformed(OrthoTransforms.ToMatrix(OrthoTransform.FlipHorizontal, 300, 200));

        Assert.Equal(0, flipped.Rotation);
        Assert.Equal(300 - text.Bounds.Right, flipped.Position.X, 1f);
    }

    [Fact]
    public void Moving_and_restyling_make_new_elements()
    {
        var stroke = Stroke(new SKPoint(0, 0), new SKPoint(10, 0));

        var moved = (StrokeElement)stroke.Moved(5, 7);
        var blue = stroke.Restyled(new MarkupStyle(SKColors.Blue, 3));

        Assert.Equal(new SKPoint(5, 7), moved.Points[0]);
        Assert.Equal(stroke.Id, moved.Id);
        Assert.Equal(SKColors.Blue, blue.Style.Color);
        Assert.Equal(SKColors.Red, stroke.Style.Color);
    }
}
