using System.Collections.Immutable;
using PicKit.Core.Documents;
using PicKit.Core.Imaging;
using PicKit.Core.Markup;
using SkiaSharp;

namespace PicKit.Tests;

public class PrivacyToolTests
{
    private static readonly SKRectI Region = new(20, 30, 60, 70);

    private static EditorDocument Document(ImageBuffer pixels) =>
        new(pixels, new DocumentSource(null, ImageFormat.Png, 0));

    private static RegionEdit Redaction(EditorDocument document, params SKRectI[] regions) =>
        RegionEdit.Prepare(
            document.Pixels,
            document.Markups,
            regions,
            (_, region) => ImageOps.SolidRegion(region.Width, region.Height, SKColors.Black));

    private static IEnumerable<(int X, int Y)> Inside(SKRectI region)
    {
        for (var y = region.Top; y < region.Bottom; y++)
        {
            for (var x = region.Left; x < region.Right; x++)
            {
                yield return (x, y);
            }
        }
    }

    [Fact]
    public void Redaction_destroys_every_original_pixel_in_the_region()
    {
        using var document = Document(TestImages.Numbered(100, 100));
        using var original = document.Pixels.Clone();

        document.ApplyRegionEdit("Redact", Redaction(document, Region));

        foreach (var (x, y) in Inside(Region))
        {
            Assert.Equal(0xFF000000, document.Pixels.Pixels[(y * 100) + x]);
        }

        // Unique pixel values show that nothing from the region survives anywhere in the image.
        var remaining = document.Pixels.Pixels.ToArray().ToHashSet();
        foreach (var (x, y) in Inside(Region))
        {
            Assert.DoesNotContain(original.Pixels[(y * 100) + x], remaining);
        }
    }

    [Fact]
    public void Redaction_is_opaque_even_over_transparency()
    {
        using var document = Document(TestImages.Solid(100, 100, SKColors.Transparent));

        document.ApplyRegionEdit("Redact", Redaction(document, Region));

        Assert.Equal(0xFF000000, document.Pixels.Pixels[(40 * 100) + 40]);
    }

    [Fact]
    public void Redaction_leaves_the_rest_of_the_image_exactly_alone()
    {
        using var document = Document(TestImages.Numbered(100, 100));
        using var original = document.Pixels.Clone();

        document.ApplyRegionEdit("Redact", Redaction(document, Region));

        for (var y = 0; y < 100; y++)
        {
            for (var x = 0; x < 100; x++)
            {
                if (!Region.Contains(x, y))
                {
                    Assert.Equal(original.Pixels[(y * 100) + x], document.Pixels.Pixels[(y * 100) + x]);
                }
            }
        }
    }

    [Fact]
    public void Undo_restores_redacted_pixels_until_the_user_commits_to_saving()
    {
        using var document = Document(TestImages.Numbered(100, 100));
        using var original = document.Pixels.Clone();
        document.ApplyRegionEdit("Redact", Redaction(document, Region));

        document.Undo();

        Assert.True(original.Pixels.SequenceEqual(document.Pixels.Pixels));
    }

    [Fact]
    public void Several_regions_are_one_undo_step()
    {
        using var document = Document(TestImages.Numbered(100, 100));
        using var original = document.Pixels.Clone();

        document.ApplyRegionEdit("Redact", Redaction(document, new SKRectI(0, 0, 10, 10), new SKRectI(80, 80, 95, 95)));

        Assert.Equal(0xFF000000, document.Pixels.Pixels[5]);
        Assert.Equal(0xFF000000, document.Pixels.Pixels[(90 * 100) + 90]);
        Assert.NotEqual(0xFF000000, document.Pixels.Pixels[(50 * 100) + 50]);

        document.Undo();
        Assert.False(document.History.CanUndo);
        Assert.True(original.Pixels.SequenceEqual(document.Pixels.Pixels));
    }

    [Fact]
    public void Markup_inside_a_redaction_cannot_float_above_it()
    {
        using var document = Document(TestImages.Solid(100, 100, SKColors.White));
        var secret = new ShapeElement
        {
            Style = new MarkupStyle(SKColors.Red, 4),
            Kind = ShapeKind.Rectangle,
            Box = new SKRect(30, 40, 50, 60),
        };
        document.SetMarkups("Add", [secret]);

        document.ApplyRegionEdit("Redact", Redaction(document, Region));

        Assert.Empty(document.Markups);
        using var exported = document.RenderFlattened();
        foreach (var (x, y) in Inside(Region))
        {
            Assert.Equal(0xFF000000, exported.Pixels[(y * 100) + x]);
        }
    }

    [Fact]
    public void Markup_elsewhere_stays_editable_through_a_redaction()
    {
        using var document = Document(TestImages.Solid(100, 100, SKColors.White));
        var keep = new ShapeElement
        {
            Style = new MarkupStyle(SKColors.Red, 2),
            Kind = ShapeKind.Rectangle,
            Box = new SKRect(80, 80, 95, 95),
        };
        document.SetMarkups("Add", [keep]);

        document.ApplyRegionEdit("Redact", Redaction(document, Region));

        Assert.Single(document.Markups);
    }

    [Fact]
    public void Undoing_a_redaction_that_burned_markup_restores_the_markup()
    {
        using var document = Document(TestImages.Solid(100, 100, SKColors.White));
        var mark = new ShapeElement
        {
            Style = new MarkupStyle(SKColors.Red, 4),
            Kind = ShapeKind.Rectangle,
            Box = new SKRect(30, 40, 50, 60),
        };
        document.SetMarkups("Add", [mark]);

        document.ApplyRegionEdit("Redact", Redaction(document, Region));
        document.Undo();

        Assert.Single(document.Markups);
        Assert.Equal(0xFFFFFFFF, document.Pixels.Pixels[(50 * 100) + 40]);
    }

    [Fact]
    public void Blur_changes_the_region_and_nothing_else()
    {
        using var source = TestImages.Photo(200, 200);
        var region = new SKRectI(50, 50, 150, 150);
        var replaced = PixelOps.CopyRegion(source, region);
        replaced.Dispose();

        using var patch = ImageOps.BlurRegion(source, region, 8);

        Assert.Equal((100, 100), (patch.Width, patch.Height));
        Assert.False(patch.HasTransparency());

        // Noise averages away, so neighbouring pixels end up much closer together.
        Assert.True(Roughness(patch) < Roughness(source) / 4);
    }

    [Fact]
    public void Blur_does_not_pull_outside_colours_into_the_region()
    {
        using var source = TestImages.Solid(100, 100, SKColors.Black);
        using (var canvas = source.CreateCanvas())
        using (var paint = new SKPaint { Color = SKColors.White })
        {
            canvas.DrawRect(40, 40, 20, 20, paint);
        }

        using var patch = ImageOps.BlurRegion(source, new SKRectI(40, 40, 60, 60), 6);

        Assert.All(patch.Pixels.ToArray(), pixel => Assert.Equal(0xFFFFFFFF, pixel));
    }

    [Fact]
    public void Pixelate_turns_each_block_into_one_colour()
    {
        using var source = TestImages.Photo(120, 120);

        using var patch = ImageOps.PixelateRegion(source, new SKRectI(0, 0, 120, 120), 10);

        for (var blockY = 0; blockY < 120; blockY += 10)
        {
            for (var blockX = 0; blockX < 120; blockX += 10)
            {
                var first = patch.Pixels[(blockY * 120) + blockX];
                for (var y = blockY; y < blockY + 10; y++)
                {
                    for (var x = blockX; x < blockX + 10; x++)
                    {
                        Assert.Equal(first, patch.Pixels[(y * 120) + x]);
                    }
                }
            }
        }
    }

    [Fact]
    public void Pixelate_averages_and_handles_uneven_edges()
    {
        using var source = TestImages.Solid(25, 13, new SKColor(10, 20, 30));

        using var patch = ImageOps.PixelateRegion(source, new SKRectI(0, 0, 25, 13), 10);

        Assert.Equal((25, 13), (patch.Width, patch.Height));
        Assert.All(patch.Pixels.ToArray(), pixel => Assert.Equal(0xFF0A141Eu, pixel));
    }

    [Fact]
    public void Covering_pixels_rounds_outward_so_edges_are_not_missed()
    {
        var region = ImageOps.CoveringPixels(new SKRect(10.9f, 20.1f, 30.1f, 40.9f), 100, 100);

        Assert.Equal(new SKRectI(10, 20, 31, 41), region);
    }

    [Fact]
    public void Covering_pixels_clamps_to_the_image()
    {
        var region = ImageOps.CoveringPixels(new SKRect(-50, -50, 500, 500), 100, 80);

        Assert.Equal(new SKRectI(0, 0, 100, 80), region);
    }

    private static double Roughness(ImageBuffer image)
    {
        double total = 0;
        var pixels = image.Pixels;
        for (var i = 1; i < pixels.Length; i++)
        {
            total += Math.Abs((int)(pixels[i] & 0xFF) - (int)(pixels[i - 1] & 0xFF));
        }

        return total / pixels.Length;
    }
}
