using System.Diagnostics;
using PicKit.Core.Documents;
using PicKit.Core.Imaging;
using PicKit.Core.Rendering;
using SkiaSharp;
using Xunit.Abstractions;

namespace PicKit.Tests;

/// <summary>A 100-megapixel image, about the largest a camera or phone produces today.</summary>
public class LargeImageTests(ITestOutputHelper output)
{
    private const int Width = 12_000;
    private const int Height = 8_400;

    private T Timed<T>(string label, Func<T> work)
    {
        var watch = Stopwatch.StartNew();
        var result = work();
        output.WriteLine($"{label}: {watch.ElapsedMilliseconds} ms");
        return result;
    }

    private static ImageBuffer Create()
    {
        var image = ImageBuffer.Create(Width, Height);
        using var canvas = image.CreateCanvas();
        using var paint = new SKPaint();
        paint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(Width, Height), [SKColors.SteelBlue, SKColors.Orange], SKShaderTileMode.Clamp);
        canvas.DrawRect(0, 0, Width, Height, paint);
        return image;
    }

    [Fact]
    public void Editing_a_100_megapixel_image_stays_practical()
    {
        using var document = new EditorDocument(Create(), new DocumentSource(null, ImageFormat.Png, 0));

        using var pyramid = new DisplayPyramid(document.Pixels);
        var preview = Timed("Build the display copy for a fitted view", () => pyramid.ForZoom(0.1));
        Assert.True(preview.Width < Width);

        var rotated = Timed("Rotate", () => OrthoTransforms.Apply(document.Pixels, OrthoTransform.Rotate90));
        document.ReplacePixels("Rotate", rotated);
        Assert.Equal(Height, document.Pixels.Width);

        var region = new SKRectI(1000, 1000, 3000, 2500);
        using (var edit = Timed("Blur a 3-megapixel area", () => RegionEdit.Prepare(
            document.Pixels, document.Markups, [region], (source, area) => ImageOps.BlurRegion(source, area, 20))))
        {
            document.ApplyRegionEdit("Blur", edit);
        }

        Timed("Undo the rotate and blur", () =>
        {
            document.Undo();
            document.Undo();
            return true;
        });
        Assert.Equal(Width, document.Pixels.Width);

        using var resized = Timed("Resize to 1920 wide", () => ImageOps.Resize(document.Pixels, 1920, 1344));
        Assert.Equal(1920, resized.Width);
    }

    [Fact]
    public void Undo_on_a_huge_image_spills_to_disk_within_budget()
    {
        const long budget = 500L * 1024 * 1024;
        using var document = new EditorDocument(Create(), new DocumentSource(null, ImageFormat.Png, 0), new EditHistory(budget));
        var original = document.Pixels.Pixels[12345];

        for (var i = 0; i < 3; i++)
        {
            document.ReplacePixels("Flip", OrthoTransforms.Apply(document.Pixels, OrthoTransform.FlipHorizontal));
        }

        Assert.True(document.History.BytesInMemory <= budget);

        document.Undo();
        document.Undo();
        document.Undo();
        Assert.Equal(original, document.Pixels.Pixels[12345]);
    }

    [Fact]
    public void An_image_that_cannot_fit_is_refused_with_a_clear_message()
    {
        var error = Assert.Throws<ImageException>(() => ImageBuffer.Create(65_000, 65_000));

        Assert.Equal(ImageErrorKind.TooLarge, error.Kind);
        Assert.Contains("too large", error.Message);
    }

    [Fact]
    public void WebP_size_limit_is_explained()
    {
        using var image = ImageBuffer.Create(ImageLimits.MaxWebPDimension + 1, 4);

        var error = Assert.Throws<ImageException>(() => ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.WebP)));

        Assert.Contains("can't be larger", error.Message);
    }
}
