using System.IO;
using System.Windows;
using System.Windows.Input;
using PicKit.Core.Imaging;
using PicKit.Core.Segmentation;
using PicKit.ViewModels;
using PicKit.ViewModels.Tools;
using SkiaSharp;

namespace PicKit.UiTests;

public class BackgroundUiTests
{
    private static async Task OpenSubject(UiHarness harness)
    {
        var path = Path.Combine(harness.Folder, "subject.jpg");
        using (var image = ImageBuffer.Create(800, 600))
        {
            image.Bitmap.Erase(new SKColor(232, 232, 228));
            using var canvas = image.CreateCanvas();
            using var paint = new SKPaint { Color = new SKColor(170, 40, 40), IsAntialias = true };
            canvas.DrawCircle(400, 300, 180, paint);
            await File.WriteAllBytesAsync(path, ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Jpeg, 95)));
        }

        harness.ViewModel.OpenPaths([path]);
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);
        harness.Pump();
    }

    private static void Paint(UiHarness harness, Point from, Point to)
    {
        var canvas = harness.Window.Canvas;
        Assert.True(canvas.PressAt(from, MouseButton.Left, ModifierKeys.None));
        for (var step = 1; step <= 6; step++)
        {
            canvas.MoveTo(new Point(from.X + ((to.X - from.X) * step / 6), from.Y + ((to.Y - from.Y) * step / 6)), ModifierKeys.None);
        }

        canvas.ReleaseAt(to, MouseButton.Left, ModifierKeys.None);
        harness.Pump();
    }

    private static Point At(UiHarness harness, float x, float y) => TestFiles.ToElement(harness.Window.Canvas, new SKPoint(x, y));

    private static uint Alpha(ImageBuffer image, int x, int y) => image.Pixels[(y * image.Width) + x] >> 24;

    [Fact]
    public void Without_the_model_it_says_so_and_the_brush_still_works() => UiHarness.Run(async harness =>
    {
        await OpenSubject(harness);
        var editor = harness.ViewModel.Editor;
        var tool = editor.UseBackgroundModel(SegmentationModel.Default with { FileName = "not-installed.onnx" });

        editor.ActiveTool = tool;
        harness.WaitFor(() => tool.State == BackgroundToolState.ModelMissing);
        harness.Pump();
        Assert.Contains("isn't installed", tool.StatusText);
        Assert.NotNull(harness.Window.Canvas.Tool);
        harness.Screenshot("background-no-model");

        tool.BrushMode = BrushMode.Remove;
        Paint(harness, At(harness, 100, 100), At(harness, 300, 100));
        tool.ApplyCommand.Execute(null);
        harness.Pump();

        var pixels = editor.Document!.Pixels;
        Assert.Equal(0u, Alpha(pixels, 200, 100));
        Assert.Equal(255u, Alpha(pixels, 400, 300));
    });

    [Fact]
    public void Manual_strokes_undo_inside_the_tool_without_touching_the_image() => UiHarness.Run(async harness =>
    {
        await OpenSubject(harness);
        var editor = harness.ViewModel.Editor;
        var tool = editor.UseBackgroundModel(SegmentationModel.Default with { FileName = "not-installed.onnx" });
        editor.ActiveTool = tool;
        harness.WaitFor(() => tool.State == BackgroundToolState.ModelMissing);

        Paint(harness, At(harness, 100, 100), At(harness, 300, 100));

        Assert.True(editor.UndoCommand.CanExecute(null));
        Assert.False(editor.Document!.History.CanUndo);

        editor.UndoCommand.Execute(null);
        Assert.False(tool.CanUndo);
        Assert.True(editor.RedoCommand.CanExecute(null));

        editor.RedoCommand.Execute(null);
        Assert.True(tool.CanUndo);
    });

    [Fact]
    public void Escape_abandons_without_changing_the_image() => UiHarness.Run(async harness =>
    {
        await OpenSubject(harness);
        var editor = harness.ViewModel.Editor;
        var tool = editor.UseBackgroundModel(SegmentationModel.Default with { FileName = "not-installed.onnx" });
        editor.ActiveTool = tool;
        harness.WaitFor(() => tool.State == BackgroundToolState.ModelMissing);
        Paint(harness, At(harness, 100, 100), At(harness, 300, 100));

        editor.CancelCommand.Execute(null);
        harness.Pump();

        Assert.Same(editor.Select, editor.ActiveTool);
        Assert.Equal(255u, Alpha(editor.Document!.Pixels, 200, 100));
        Assert.False(editor.Document.History.CanUndo);
    });

    [Fact]
    public void A_mask_left_on_screen_is_what_gets_saved() => UiHarness.Run(async harness =>
    {
        await OpenSubject(harness);
        var editor = harness.ViewModel.Editor;
        var tool = editor.UseBackgroundModel(SegmentationModel.Default with { FileName = "not-installed.onnx" });
        editor.ActiveTool = tool;
        harness.WaitFor(() => tool.State == BackgroundToolState.ModelMissing);
        Paint(harness, At(harness, 100, 100), At(harness, 300, 100));

        var target = Path.Combine(harness.Folder, "cut.png");
        harness.Dialogs.SavePath = target;
        await harness.ViewModel.SaveAsCommand.ExecuteAsync(null);
        harness.Pump();

        using var saved = ImageDecoder.Decode(target);
        Assert.Equal(0u, Alpha(saved.Pixels, 200, 100));
    });

    [ModelFact]
    public void Automatic_removal_then_brush_then_transparent_png() => UiHarness.Run(async harness =>
    {
        await OpenSubject(harness);
        var editor = harness.ViewModel.Editor;

        editor.ActiveTool = editor.Background;
        harness.Pump();
        Assert.Equal(BackgroundToolState.Processing, editor.Background.State);
        harness.Screenshot("background-processing");

        harness.WaitFor(() => editor.Background.State == BackgroundToolState.Ready, seconds: 60);
        harness.Pump();
        harness.Screenshot("background-ready");

        // Paint part of the subject back out to show the brush works on the real result.
        editor.Background.BrushMode = BrushMode.Remove;
        Paint(harness, At(harness, 350, 300), At(harness, 450, 300));
        harness.Screenshot("background-brushed");

        editor.Background.ApplyCommand.Execute(null);
        harness.Pump();

        var pixels = editor.Document!.Pixels;
        Assert.True(Alpha(pixels, 20, 20) < 30, $"background alpha {Alpha(pixels, 20, 20)}");
        Assert.Equal(255u, Alpha(pixels, 400, 200));
        Assert.Equal(0u, Alpha(pixels, 400, 300));

        // Undo returns the original in one step.
        Assert.Equal("Remove background", editor.Document.History.UndoLabel);
        editor.UndoCommand.Execute(null);
        harness.Pump();
        Assert.Equal(255u, Alpha(editor.Document.Pixels, 20, 20));

        editor.RedoCommand.Execute(null);
        harness.Pump();

        // This was a JPEG, which can't be transparent, so saving must steer to PNG.
        var target = Path.Combine(harness.Folder, "subject-transparent.png");
        harness.Dialogs.SavePath = target;
        await harness.ViewModel.SaveCommand.ExecuteAsync(null);
        harness.Pump();

        using var saved = ImageDecoder.Decode(target);
        Assert.Equal(ImageFormat.Png, saved.Format);
        Assert.True(Alpha(saved.Pixels, 20, 20) < 30);
    });

    [ModelFact]
    public void Leaving_during_processing_cancels_cleanly() => UiHarness.Run(async harness =>
    {
        await OpenSubject(harness);
        var editor = harness.ViewModel.Editor;

        editor.ActiveTool = editor.Background;
        harness.Pump();
        editor.CancelCommand.Execute(null);
        harness.Pump();

        Assert.Same(editor.Select, editor.ActiveTool);
        Assert.Equal(255u, Alpha(editor.Document!.Pixels, 20, 20));

        await Task.Delay(100);
        harness.Pump();
    });
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
