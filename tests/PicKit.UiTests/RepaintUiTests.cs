using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PicKit.Controls;
using PicKit.ViewModels;
using SkiaSharp;

namespace PicKit.UiTests;

/// <summary>Reads back what the canvas is really showing, not just what the tool believes.</summary>
public class RepaintUiTests
{
    private static int BrightnessAt(ImageCanvas canvas, Point point)
    {
        var bitmap = new RenderTargetBitmap((int)canvas.ActualWidth, (int)canvas.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)point.X, (int)point.Y, 1, 1), pixel, 4, 0);
        return pixel[0] + pixel[1] + pixel[2];
    }

    [Fact]
    public void Switching_destination_moves_the_crop_box_on_screen_straight_away() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1920, 1080);
        var editor = harness.ViewModel.Editor;
        editor.SelectedTab = EditorTab.ResizeFor;
        harness.Pump();

        var canvas = harness.Window.Canvas;
        var presets = editor.Presets.Groups.SelectMany(group => group.Presets).ToList();

        // Inside a centred square crop, but outside a tall 9:16 one.
        var probe = TestFiles.ToElement(canvas, new SKPoint(500, 300));

        editor.Presets.SelectedPreset = presets.First(preset => preset.Name == "Instagram Post");
        harness.Pump();
        var insideCrop = BrightnessAt(canvas, probe);

        editor.Presets.SelectedPreset = presets.First(preset => preset.Name == "Instagram Story");
        harness.Pump();
        var outsideCrop = BrightnessAt(canvas, probe);

        Assert.True(outsideCrop < insideCrop * 0.6, $"Expected the area to dim: {insideCrop} then {outsideCrop}.");
    });

    [Fact]
    public void Changing_crop_shape_redraws_the_box_on_screen_straight_away() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1920, 1080);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Crop;
        harness.Pump();

        var canvas = harness.Window.Canvas;
        var probe = TestFiles.ToElement(canvas, new SKPoint(300, 300));
        var uncropped = BrightnessAt(canvas, probe);

        editor.Crop.SelectedAspect = editor.Crop.Aspects.First(aspect => aspect.Label == "1:1");
        harness.Pump();

        Assert.True(BrightnessAt(canvas, probe) < uncropped * 0.6);
    });
}
