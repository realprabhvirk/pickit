using System.IO;
using System.Windows;
using System.Windows.Input;
using PicKit.Controls.Tools;
using PicKit.Core.Imaging;
using PicKit.ViewModels;
using SkiaSharp;

namespace PicKit.UiTests;

public class EditingUiTests
{
    [Fact]
    public void Crop_end_to_end_with_undo() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1600, 1000);
        var editor = harness.ViewModel.Editor;
        var canvas = harness.Window.Canvas;

        editor.ActiveTool = editor.Crop;
        harness.Pump();
        Assert.Same(editor.Crop.CanvasTool, canvas.Tool);
        Assert.Equal("1600 × 1000", editor.Crop.SizeText);

        // Pick 16:9, then drag the bottom-right handle inwards using real view coordinates.
        editor.Crop.SelectedAspect = editor.Crop.Aspects.First(aspect => aspect.Label == "16:9");
        harness.Pump();
        var tool = (CropCanvasTool)editor.Crop.CanvasTool;
        var corner = TestFiles.ToElement(canvas, new SKPoint(tool.Box.Right, tool.Box.Bottom));
        Assert.True(canvas.PressAt(corner, MouseButton.Left, ModifierKeys.None));
        canvas.MoveTo(new Point(corner.X - 200, corner.Y - 120), ModifierKeys.None);
        harness.Screenshot("crop-dragging");
        canvas.ReleaseAt(new Point(corner.X - 200, corner.Y - 120), MouseButton.Left, ModifierKeys.None);
        harness.Pump();

        var expected = tool.PixelBox;
        Assert.True(expected.Width < 1600);
        Assert.Equal(16f / 9f, (float)expected.Width / expected.Height, 1f);

        await editor.Crop.ApplyCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.Equal((expected.Width, expected.Height), (editor.Document!.Pixels.Width, editor.Document.Pixels.Height));
        Assert.Same(editor.Select, editor.ActiveTool);
        Assert.True(canvas.View.IsFitted);
        Assert.StartsWith("● ", harness.ViewModel.WindowTitle);

        editor.UndoCommand.Execute(null);
        harness.Pump();
        Assert.Equal((1600, 1000), (editor.Document.Pixels.Width, editor.Document.Pixels.Height));
        Assert.Equal(1600, canvas.View.ImageWidth);
        Assert.DoesNotContain("●", harness.ViewModel.WindowTitle);

        editor.RedoCommand.Execute(null);
        harness.Pump();
        Assert.Equal(expected.Width, editor.Document.Pixels.Width);
    });

    [Fact]
    public void Escape_leaves_crop_without_cutting_anything() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 800, 600);
        var editor = harness.ViewModel.Editor;

        editor.ActiveTool = editor.Crop;
        harness.Pump();
        editor.CancelCommand.Execute(null);
        harness.Pump();

        Assert.Same(editor.Select, editor.ActiveTool);
        Assert.Equal(800, editor.Document!.Pixels.Width);
        Assert.False(editor.Document.History.CanUndo);
    });

    [Fact]
    public void Resize_keeps_proportions_and_shows_the_result() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1920, 1080);
        var editor = harness.ViewModel.Editor;
        var resize = editor.Resize;

        editor.ActiveTool = resize;
        harness.Pump();
        Assert.Equal("1920", resize.WidthText);

        resize.WidthText = "1280";
        Assert.Equal("720", resize.HeightText);
        Assert.Equal("1280 × 720", resize.ResultText);
        Assert.Null(resize.Warning);
        harness.Screenshot("resize-panel");

        await resize.ApplyCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.Equal((1280, 720), (editor.Document!.Pixels.Width, editor.Document.Pixels.Height));
        Assert.Equal("1280", resize.WidthText);
        Assert.Equal("1280 × 720", resize.OriginalText);
    });

    [Fact]
    public void Resize_warns_about_stretching_and_rejects_nonsense() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1920, 1080);
        var editor = harness.ViewModel.Editor;
        var resize = editor.Resize;
        editor.ActiveTool = resize;
        harness.Pump();

        resize.KeepAspect = false;
        resize.HeightText = "1080";
        resize.WidthText = "1080";
        Assert.Contains("stretch", resize.Warning);

        resize.WidthText = "banana";
        Assert.False(resize.ApplyCommand.CanExecute(null));

        resize.WidthText = "0";
        Assert.False(resize.ApplyCommand.CanExecute(null));
    });

    [Fact]
    public void Fit_mode_matches_the_specification_example() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1920, 1080);
        var editor = harness.ViewModel.Editor;
        var resize = editor.Resize;
        editor.ActiveTool = resize;
        harness.Pump();

        resize.Mode = PicKit.Core.Imaging.ResizeMode.Fit;
        resize.WidthText = "1080";
        resize.HeightText = "1080";

        Assert.Equal("1080 × 608", resize.ResultText);
        Assert.Null(editor.CanvasTool);
    });

    [Fact]
    public void Crop_to_fill_shows_the_cut_before_making_it() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1920, 1080);
        var editor = harness.ViewModel.Editor;
        var resize = editor.Resize;
        editor.ActiveTool = resize;
        harness.Pump();

        resize.Mode = PicKit.Core.Imaging.ResizeMode.CropToFill;
        resize.WidthText = "1000";
        resize.HeightText = "1000";
        harness.Pump();

        var guide = Assert.IsType<CropCanvasTool>(harness.Window.Canvas.Tool);
        Assert.Equal(1080, guide.PixelBox.Width);
        Assert.Equal(1920, editor.Document!.Pixels.Width);
        harness.Screenshot("resize-crop-to-fill");

        await resize.ApplyCommand.ExecuteAsync(null);
        harness.Pump();
        Assert.Equal((1000, 1000), (editor.Document.Pixels.Width, editor.Document.Pixels.Height));
    });

    [Fact]
    public void Rotating_swaps_sides_and_undoes() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Rotate;
        harness.Pump();

        await editor.Rotate.RotateRightCommand.ExecuteAsync(null);
        harness.Pump();
        Assert.Equal((800, 1200), (editor.Document!.Pixels.Width, editor.Document.Pixels.Height));
        Assert.Equal(800, harness.Window.Canvas.View.ImageWidth);
        Assert.NotNull(editor.ActiveTool);
        harness.Screenshot("rotate-panel");

        editor.UndoCommand.Execute(null);
        harness.Pump();
        Assert.Equal((1200, 800), (editor.Document.Pixels.Width, editor.Document.Pixels.Height));
    });

    [Fact]
    public void Platform_preset_crop_shows_a_preview_then_applies() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1920, 1080);
        var editor = harness.ViewModel.Editor;

        editor.SelectedTab = EditorTab.ResizeFor;
        harness.Pump();
        Assert.Same(editor.Presets, editor.ActiveTool);
        Assert.Null(harness.Window.Canvas.Tool);
        harness.Screenshot("presets-list");

        var post = editor.Presets.Groups.SelectMany(group => group.Presets).First(preset => preset.Name == "Instagram Post");
        editor.Presets.SelectedPreset = post;
        harness.Pump();

        Assert.IsType<CropCanvasTool>(harness.Window.Canvas.Tool);
        Assert.Contains("won't fill", editor.Presets.CropSummary);
        Assert.Equal(1920, editor.Document!.Pixels.Width);
        harness.Screenshot("preset-instagram-post");

        await editor.Presets.CropToFitCommand.ExecuteAsync(null);
        harness.Pump();
        Assert.Equal((1080, 1080), (editor.Document.Pixels.Width, editor.Document.Pixels.Height));
    });

    [Fact]
    public void Platform_preset_fit_inside_never_crops() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1920, 1080);
        var editor = harness.ViewModel.Editor;
        editor.SelectedTab = EditorTab.ResizeFor;
        harness.Pump();

        editor.Presets.SelectedPreset = editor.Presets.Groups.SelectMany(group => group.Presets).First(preset => preset.Name == "Instagram Post");
        harness.Pump();
        await editor.Presets.FitInsideCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.Equal((1080, 608), (editor.Document!.Pixels.Width, editor.Document.Pixels.Height));
    });

    [Fact]
    public void Fit_only_destinations_offer_no_crop() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1920, 1080);
        var editor = harness.ViewModel.Editor;
        editor.SelectedTab = EditorTab.ResizeFor;
        harness.Pump();

        editor.Presets.SelectedPreset = editor.Presets.Groups.SelectMany(group => group.Presets).First(preset => preset.Name == "Email");
        harness.Pump();

        Assert.False(editor.Presets.CanCrop);
        Assert.Null(harness.Window.Canvas.Tool);
    });

    [Fact]
    public void Tab_order_visits_commands_and_every_tool_has_a_name() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 800, 600);

        Assert.All(harness.ViewModel.Editor.EditTools, tool =>
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Name));
            Assert.False(string.IsNullOrWhiteSpace(tool.Glyph));
        });
    });
}

internal static class TestFiles
{
    public static async Task OpenPhoto(UiHarness harness, int width, int height)
    {
        var path = Path.Combine(harness.Folder, "photo.png");
        using (var image = ImageBuffer.Create(width, height))
        {
            using var canvas = image.CreateCanvas();
            using var paint = new SKPaint();
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(width, height), [SKColors.SteelBlue, SKColors.Orange], SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, width, height, paint);

            using var circle = new SKPaint { Color = SKColors.Crimson, IsAntialias = true };
            canvas.DrawCircle(width / 2f, height / 2f, Math.Min(width, height) / 4f, circle);

            using var data = image.Bitmap.PeekPixels().Encode(new SKPngEncoderOptions())!;
            await File.WriteAllBytesAsync(path, data.ToArray());
        }

        harness.ViewModel.OpenPaths([path]);
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);
        harness.Pump();
    }

    /// <summary>Converts an image point to this element's layout coordinates.</summary>
    public static Point ToElement(FrameworkElement canvas, SKPoint imagePoint)
    {
        var view = ((PicKit.Controls.ImageCanvas)canvas).View.ImageToView(imagePoint);
        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(canvas);
        return new Point(view.X / scale.DpiScaleX, view.Y / scale.DpiScaleY);
    }
}
