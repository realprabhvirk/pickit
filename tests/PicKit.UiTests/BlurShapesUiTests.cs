using System.Windows;
using System.Windows.Input;
using PicKit.Core.Documents;
using PicKit.Core.Imaging;
using PicKit.ViewModels;
using PicKit.ViewModels.Tools;
using SkiaSharp;

namespace PicKit.UiTests;

public class BlurShapesUiTests
{
    private static uint[] Snapshot(ImageBuffer image) => image.Pixels.ToArray();

    private static bool Changed(uint[] before, ImageBuffer image, int x, int y) =>
        before[(y * image.Width) + x] != image.Pixels[(y * image.Width) + x];

    private static async Task<EditorViewModel> OpenTool(UiHarness harness, RegionEffectToolViewModel? tool = null)
    {
        await OpenNoise(harness);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = tool ?? editor.Blur;
        harness.Pump();
        return editor;
    }

    /// <summary>Busy pixels, so any blur shows up at the exact point it reached.</summary>
    private static async Task OpenNoise(UiHarness harness)
    {
        var path = Path.Combine(harness.Folder, "noise.png");
        using (var image = ImageBuffer.Create(1000, 800))
        {
            var random = new Random(4);
            var pixels = image.Pixels;
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = 0xFF000000 | (uint)random.Next(0xFFFFFF);
            }

            File.WriteAllBytes(path, ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png)));
        }

        harness.ViewModel.OpenPaths([path]);
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);
        harness.Pump();
    }

    private static void Choose(RegionEffectToolViewModel tool, RegionShape shape) =>
        tool.SelectedShape = tool.Shapes.First(choice => choice.Shape == shape);

    private static void Drag(UiHarness harness, params SKPoint[] points)
    {
        var canvas = harness.Window.Canvas;
        var first = TestFiles.ToElement(canvas, points[0]);
        Assert.True(canvas.PressAt(first, MouseButton.Left, ModifierKeys.None));
        foreach (var point in points.Skip(1))
        {
            canvas.MoveTo(TestFiles.ToElement(canvas, point), ModifierKeys.None);
        }

        canvas.ReleaseAt(TestFiles.ToElement(canvas, points[^1]), MouseButton.Left, ModifierKeys.None);
    }

    private static void WaitForEdit(UiHarness harness)
    {
        harness.WaitFor(() => harness.ViewModel.Editor.IsIdle);
        harness.Pump();
    }

    [Fact]
    public void A_box_is_still_the_default_and_applies_on_release() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTool(harness);
        var pixels = editor.Document!.Pixels;
        var before = Snapshot(pixels);
        Assert.Equal(RegionShape.Box, editor.Blur.SelectedShape.Shape);
        Assert.False(editor.Blur.ShowsBrushSize);

        Drag(harness, new SKPoint(200, 200), new SKPoint(600, 500));
        WaitForEdit(harness);

        // Right into the corner of the box, because a box has no rounding.
        Assert.True(Changed(before, editor.Document.Pixels, 206, 206));
        Assert.False(Changed(before, editor.Document.Pixels, 150, 150));
    });

    [Fact]
    public void A_circle_blurs_its_middle_and_leaves_the_corners_of_its_box() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTool(harness);
        var before = Snapshot(editor.Document!.Pixels);
        Choose(editor.Blur, RegionShape.Ellipse);

        Drag(harness, new SKPoint(200, 200), new SKPoint(600, 600));
        WaitForEdit(harness);

        var pixels = editor.Document.Pixels;
        Assert.True(Changed(before, pixels, 400, 400));
        Assert.False(Changed(before, pixels, 212, 212));
        Assert.False(Changed(before, pixels, 588, 588));
        harness.Screenshot("blur-circle");
    });

    [Fact]
    public void A_rounded_box_fills_its_sides_but_not_its_corners() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTool(harness);
        var before = Snapshot(editor.Document!.Pixels);
        Choose(editor.Blur, RegionShape.Rounded);

        Drag(harness, new SKPoint(200, 200), new SKPoint(600, 600));
        WaitForEdit(harness);

        var pixels = editor.Document.Pixels;
        Assert.True(Changed(before, pixels, 400, 206));
        Assert.False(Changed(before, pixels, 202, 202));
    });

    [Fact]
    public void The_brush_blurs_only_along_its_stroke() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTool(harness);
        var before = Snapshot(editor.Document!.Pixels);
        Choose(editor.Blur, RegionShape.Brush);
        Assert.True(editor.Blur.ShowsBrushSize);
        Assert.Contains("Paint", editor.Blur.ShapeHint);
        editor.Blur.BrushSize = 40;
        harness.Pump();

        Drag(harness, new SKPoint(200, 300), new SKPoint(400, 300), new SKPoint(600, 300));
        WaitForEdit(harness);

        var pixels = editor.Document.Pixels;
        Assert.True(Changed(before, pixels, 400, 300));
        Assert.True(Changed(before, pixels, 400, 312));
        Assert.False(Changed(before, pixels, 400, 340));
        Assert.False(Changed(before, pixels, 400, 260));
        harness.Screenshot("blur-brush");
    });

    [Fact]
    public void A_single_click_with_the_brush_leaves_a_dot() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTool(harness);
        var before = Snapshot(editor.Document!.Pixels);
        Choose(editor.Blur, RegionShape.Brush);

        Drag(harness, new SKPoint(400, 300));
        WaitForEdit(harness);

        Assert.True(Changed(before, editor.Document.Pixels, 400, 300));
    });

    [Fact]
    public void Each_shape_is_one_undo_step() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTool(harness);
        var document = editor.Document!;
        var before = Snapshot(document.Pixels);
        Choose(editor.Blur, RegionShape.Ellipse);
        Drag(harness, new SKPoint(200, 200), new SKPoint(600, 600));
        WaitForEdit(harness);

        editor.UndoCommand.Execute(null);
        harness.Pump();

        Assert.True(before.AsSpan().SequenceEqual(document.Pixels.Pixels));
    });

    [Fact]
    public void A_shape_running_off_the_image_stops_at_the_edge() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTool(harness);
        var before = Snapshot(editor.Document!.Pixels);
        Choose(editor.Blur, RegionShape.Brush);

        Drag(harness, new SKPoint(5, 400), new SKPoint(-200, 400));
        WaitForEdit(harness);

        Assert.True(Changed(before, editor.Document.Pixels, 2, 400));
        Assert.Equal((1000, 800), (editor.Document.Pixels.Width, editor.Document.Pixels.Height));
    });

    [Fact]
    public void Markup_under_a_circle_is_burned_in_so_it_cannot_stay_readable() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTool(harness);
        var document = editor.Document!;
        document.SetMarkups("Add", [new PicKit.Core.Markup.TextElement
        {
            Style = new PicKit.Core.Markup.MarkupStyle(SKColors.Red, 2),
            Position = new SKPoint(380, 380),
            Text = "Secret",
            FontSize = 30,
        }]);

        Choose(editor.Blur, RegionShape.Ellipse);
        Drag(harness, new SKPoint(200, 200), new SKPoint(600, 600));
        WaitForEdit(harness);

        Assert.Empty(document.Markups);
    });

    [Fact]
    public void Pixelate_gets_the_same_shapes() => UiHarness.Run(async harness =>
    {
        await OpenNoise(harness);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Pixelate;
        harness.Pump();
        var before = Snapshot(editor.Document!.Pixels);

        Choose(editor.Pixelate, RegionShape.Ellipse);
        Drag(harness, new SKPoint(200, 200), new SKPoint(600, 600));
        WaitForEdit(harness);

        Assert.True(Changed(before, editor.Document.Pixels, 400, 400));
        Assert.False(Changed(before, editor.Document.Pixels, 212, 212));
    });

    [Fact]
    public void The_panel_offers_the_shapes_and_shows_brush_size_only_for_the_brush() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTool(harness);
        harness.Screenshot("blur-panel-box");

        Choose(editor.Blur, RegionShape.Brush);
        harness.Pump();
        harness.Screenshot("blur-panel-brush");

        Assert.Equal(["Box", "Rounded", "Circle", "Brush"], editor.Blur.Shapes.Select(shape => shape.Label));
    });
}
