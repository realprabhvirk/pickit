using Microsoft.ML.OnnxRuntime.Tensors;
using PicKit.Core.Imaging;
using PicKit.Core.Segmentation;
using SkiaSharp;

namespace PicKit.Tests;

public class BackgroundRemovalTests
{
    private static byte AlphaAt(ImageBuffer image, int x, int y) => (byte)(image.Pixels[(y * image.Width) + x] >> 24);

    [Fact]
    public void Preprocess_produces_planar_rgb_at_the_network_size()
    {
        using var image = TestImages.Solid(100, 60, new SKColor(255, 0, 0));
        var model = SegmentationModel.Default with { InputSize = 32 };

        var tensor = BackgroundSegmenter.Preprocess(image, model);

        Assert.Equal([1, 3, 32, 32], tensor.Dimensions.ToArray());
        Assert.Equal(0.5f, tensor[0, 0, 10, 10], 2f);
        Assert.Equal(-0.5f, tensor[0, 1, 10, 10], 2f);
        Assert.Equal(-0.5f, tensor[0, 2, 10, 10], 2f);
    }

    [Fact]
    public void Preprocess_places_transparency_on_white()
    {
        using var image = TestImages.Solid(32, 32, SKColors.Transparent);
        var model = SegmentationModel.Default with { InputSize = 16 };

        var tensor = BackgroundSegmenter.Preprocess(image, model);

        Assert.Equal(0.5f, tensor[0, 0, 8, 8], 2f);
    }

    [Fact]
    public void Output_stretches_to_full_range_and_removes_haze()
    {
        var output = new DenseTensor<float>([1, 1, 2, 2]);
        output[0, 0, 0, 0] = 0.2f;
        output[0, 0, 0, 1] = 0.21f;
        output[0, 0, 1, 0] = 0.8f;
        output[0, 0, 1, 1] = 0.5f;

        using var mask = BackgroundSegmenter.ToMask(output, 2);

        Assert.Equal(0, AlphaAt(mask, 0, 0));
        Assert.Equal(0, AlphaAt(mask, 1, 0));
        Assert.Equal(255, AlphaAt(mask, 0, 1));
        Assert.InRange(AlphaAt(mask, 1, 1), 100, 155);
    }

    [Fact]
    public void Flat_output_does_not_divide_by_zero()
    {
        var output = new DenseTensor<float>([1, 1, 2, 2]);

        using var mask = BackgroundSegmenter.ToMask(output, 2);

        Assert.Equal(0, AlphaAt(mask, 0, 0));
    }

    [Fact]
    public void Applying_a_mask_makes_real_transparency()
    {
        using var image = TestImages.Solid(4, 1, SKColors.Red);
        using var mask = ImageBuffer.Create(4, 1);
        mask.Pixels[0] = BackgroundSegmenter.MaskPixel(255);
        mask.Pixels[1] = BackgroundSegmenter.MaskPixel(128);
        mask.Pixels[2] = BackgroundSegmenter.MaskPixel(0);
        mask.Pixels[3] = BackgroundSegmenter.MaskPixel(0);

        using var result = ImageOps.ApplyMask(image, mask);

        Assert.Equal(0xFFFF0000, result.Pixels[0]);
        Assert.Equal(128u, result.Pixels[1] >> 24);
        Assert.Equal(0u, result.Pixels[2]);
    }

    [Fact]
    public void Masked_image_stays_transparent_through_a_png()
    {
        using var image = TestImages.Solid(16, 16, SKColors.Red);
        using var mask = ImageBuffer.Create(16, 16);
        using var result = ImageOps.ApplyMask(image, mask);

        var png = ImageEncoder.Encode(result, new EncodeOptions(ImageFormat.Png));
        using var decoded = ImageDecoder.Decode(png);

        Assert.Equal(0u, decoded.Pixels.Pixels[0] >> 24);
    }

    [Fact]
    public void Remove_brush_clears_the_mask_and_keep_brings_it_back()
    {
        using var editor = new MaskEditor(MaskEditor.KeepEverything(100, 100));

        editor.BeginStroke(new SKPoint(50, 50), 10, BrushMode.Remove);
        editor.EndStroke();
        Assert.Equal(0, AlphaAt(editor.Mask, 50, 50));
        Assert.Equal(255, AlphaAt(editor.Mask, 5, 5));

        editor.BeginStroke(new SKPoint(50, 50), 10, BrushMode.Keep);
        editor.EndStroke();
        Assert.Equal(255, AlphaAt(editor.Mask, 50, 50));
    }

    [Fact]
    public void A_stroke_follows_its_path()
    {
        using var editor = new MaskEditor(MaskEditor.KeepEverything(200, 100));

        editor.BeginStroke(new SKPoint(20, 50), 8, BrushMode.Remove);
        editor.ContinueStroke(new SKPoint(100, 50));
        editor.ContinueStroke(new SKPoint(180, 50));
        editor.EndStroke();

        Assert.Equal(0, AlphaAt(editor.Mask, 60, 50));
        Assert.Equal(0, AlphaAt(editor.Mask, 140, 50));
        Assert.Equal(255, AlphaAt(editor.Mask, 100, 10));
    }

    [Fact]
    public void Undo_and_redo_are_exact_and_cost_only_the_stroke()
    {
        using var editor = new MaskEditor(MaskEditor.KeepEverything(100, 100));
        using var before = editor.Mask.Clone();

        editor.BeginStroke(new SKPoint(50, 50), 10, BrushMode.Remove);
        editor.ContinueStroke(new SKPoint(60, 60));
        editor.EndStroke();
        using var after = editor.Mask.Clone();

        editor.Undo();
        Assert.True(before.Pixels.SequenceEqual(editor.Mask.Pixels));
        Assert.True(editor.CanRedo);

        editor.Redo();
        Assert.True(after.Pixels.SequenceEqual(editor.Mask.Pixels));
    }

    [Fact]
    public void A_new_stroke_discards_redo()
    {
        using var editor = new MaskEditor(MaskEditor.KeepEverything(100, 100));
        editor.BeginStroke(new SKPoint(50, 50), 10, BrushMode.Remove);
        editor.EndStroke();
        editor.Undo();

        editor.BeginStroke(new SKPoint(20, 20), 10, BrushMode.Remove);
        editor.EndStroke();

        Assert.False(editor.CanRedo);
    }

    [Fact]
    public void Restore_returns_to_the_automatic_result_and_can_itself_be_undone()
    {
        using var automatic = TestImages.Solid(50, 50, SKColors.Transparent);
        automatic.Pixels[0] = BackgroundSegmenter.MaskPixel(200);
        using var editor = new MaskEditor(automatic.Clone());

        editor.BeginStroke(new SKPoint(25, 25), 10, BrushMode.Keep);
        editor.EndStroke();
        using var edited = editor.Mask.Clone();

        editor.Restore();
        Assert.True(automatic.Pixels.SequenceEqual(editor.Mask.Pixels));

        editor.Undo();
        Assert.True(edited.Pixels.SequenceEqual(editor.Mask.Pixels));
    }

    [Fact]
    public void Strokes_report_only_the_area_they_touched()
    {
        using var editor = new MaskEditor(MaskEditor.KeepEverything(1000, 1000));
        SKRectI? region = null;
        editor.Changed += changed => region = changed;

        editor.BeginStroke(new SKPoint(500, 500), 10, BrushMode.Remove);

        Assert.NotNull(region);
        Assert.True(region.Value.Width < 40 && region.Value.Height < 40);
        Assert.True(region.Value.Contains(500, 500));
    }

    [Fact]
    public void Strokes_at_the_edge_stay_within_the_image()
    {
        using var editor = new MaskEditor(MaskEditor.KeepEverything(50, 50));

        editor.BeginStroke(new SKPoint(0, 0), 30, BrushMode.Remove);
        editor.ContinueStroke(new SKPoint(-20, 60));
        editor.EndStroke();

        Assert.Equal(0, AlphaAt(editor.Mask, 0, 0));
    }

    [ModelFact]
    public void Real_model_finds_a_subject_on_a_plain_background()
    {
        using var image = TestImages.Solid(640, 480, new SKColor(235, 235, 235));
        using (var canvas = image.CreateCanvas())
        using (var paint = new SKPaint { Color = new SKColor(180, 30, 30), IsAntialias = true })
        {
            canvas.DrawCircle(320, 240, 150, paint);
        }

        using var segmenter = new BackgroundSegmenter(SegmentationModel.Default, SegmentationModel.Default.Locate()!);
        using var mask = segmenter.CreateMask(image);

        Assert.Equal((640, 480), (mask.Width, mask.Height));
        Assert.True(AlphaAt(mask, 320, 240) > 200, $"centre alpha {AlphaAt(mask, 320, 240)}");
        Assert.True(AlphaAt(mask, 10, 10) < 40, $"corner alpha {AlphaAt(mask, 10, 10)}");
    }

    [ModelFact]
    public void Model_can_be_cancelled()
    {
        using var image = TestImages.Solid(64, 64, SKColors.Gray);
        using var segmenter = new BackgroundSegmenter(SegmentationModel.Default, SegmentationModel.Default.Locate()!);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => segmenter.CreateMask(image, cancelled.Token));
    }

    [Fact]
    public void Model_file_lookup_reports_absence_rather_than_guessing()
    {
        var missing = SegmentationModel.Default with { FileName = "no-such-model.onnx" };

        Assert.Null(missing.Locate());
    }
}

/// <summary>A test that needs the real model, which isn't in the repository.</summary>
public sealed class ModelFactAttribute : FactAttribute
{
    public ModelFactAttribute()
    {
        if (SegmentationModel.Default.Locate() is null)
        {
            Skip = "The segmentation model isn't installed. Run tools/get-model.ps1.";
        }
    }
}
