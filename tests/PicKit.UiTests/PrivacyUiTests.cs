using System.Windows;
using System.Windows.Input;
using PicKit.Controls.Tools;
using SkiaSharp;

namespace PicKit.UiTests;

public class PrivacyUiTests
{
    private static void Drag(UiHarness harness, Point from, Point to)
    {
        var canvas = harness.Window.Canvas;
        Assert.True(canvas.PressAt(from, MouseButton.Left, ModifierKeys.None));
        canvas.MoveTo(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2), ModifierKeys.None);
        canvas.MoveTo(to, ModifierKeys.None);
        canvas.ReleaseAt(to, MouseButton.Left, ModifierKeys.None);
        harness.Pump();
    }

    private static Point At(UiHarness harness, float x, float y) => TestFiles.ToElement(harness.Window.Canvas, new SKPoint(x, y));

    private static uint Pixel(UiHarness harness, int x, int y)
    {
        var pixels = harness.ViewModel.Editor.Document!.Pixels;
        return pixels.Pixels[(y * pixels.Width) + x];
    }

    [Fact]
    public void Blur_applies_on_release_and_undoes() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        var before = Pixel(harness, 600, 200);
        editor.ActiveTool = editor.Blur;
        harness.Pump();

        Drag(harness, At(harness, 300, 250), At(harness, 900, 550));
        harness.WaitFor(() => editor.IsIdle && editor.Document!.History.CanUndo);
        harness.Screenshot("blur");

        Assert.Equal("Blur", editor.Document!.History.UndoLabel);
        Assert.Equal(1200, editor.Document.Pixels.Width);

        editor.UndoCommand.Execute(null);
        harness.Pump();
        Assert.Equal(before, Pixel(harness, 600, 200));
    });

    [Fact]
    public void Pixelate_applies_on_release() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Pixelate;
        harness.Pump();

        Drag(harness, At(harness, 300, 250), At(harness, 900, 550));
        harness.WaitFor(() => editor.IsIdle && editor.Document!.History.CanUndo);
        harness.Screenshot("pixelate");

        Assert.Equal("Pixelate", editor.Document!.History.UndoLabel);
    });

    [Fact]
    public void Redact_marks_then_apply_destroys_the_pixels() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        var redact = editor.Redact;
        var original = Pixel(harness, 600, 400);
        editor.ActiveTool = redact;
        harness.Pump();

        Drag(harness, At(harness, 500, 300), At(harness, 700, 500));
        Assert.True(redact.HasPending);
        Assert.Equal(original, Pixel(harness, 600, 400));
        harness.Screenshot("redact-pending");

        redact.ApplyCommand.Execute(null);
        harness.Pump();

        Assert.False(redact.HasPending);
        Assert.Equal(0xFF000000, Pixel(harness, 600, 400));
        Assert.Equal("Redact", editor.Document!.History.UndoLabel);
        harness.Screenshot("redact-applied");

        editor.UndoCommand.Execute(null);
        harness.Pump();
        Assert.Equal(original, Pixel(harness, 600, 400));
    });

    [Fact]
    public void Leaving_the_tool_applies_marks_rather_than_dropping_them() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Redact;
        harness.Pump();
        Drag(harness, At(harness, 500, 300), At(harness, 700, 500));

        editor.ActiveTool = editor.Crop;
        harness.Pump();

        Assert.Equal(0xFF000000, Pixel(harness, 600, 400));
    });

    [Fact]
    public void Flushing_before_an_export_applies_marks() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Redact;
        harness.Pump();
        Drag(harness, At(harness, 500, 300), At(harness, 700, 500));

        editor.FlushPendingEdits();
        harness.Pump();

        using var exported = editor.Document!.RenderFlattened();
        Assert.Equal(0xFF000000, exported.Pixels[(400 * exported.Width) + 600]);
    });

    [Fact]
    public void Escape_clears_marks_first_then_leaves_the_tool() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        var original = Pixel(harness, 600, 400);
        editor.ActiveTool = editor.Redact;
        harness.Pump();
        Drag(harness, At(harness, 500, 300), At(harness, 700, 500));

        editor.CancelCommand.Execute(null);
        harness.Pump();
        Assert.False(editor.Redact.HasPending);
        Assert.Same(editor.Redact, editor.ActiveTool);

        editor.CancelCommand.Execute(null);
        harness.Pump();
        Assert.Same(editor.Select, editor.ActiveTool);
        Assert.Equal(original, Pixel(harness, 600, 400));
    });

    [Fact]
    public void White_redaction_fills_white() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Redact;
        editor.Redact.UseWhite = true;
        harness.Pump();
        Drag(harness, At(harness, 500, 300), At(harness, 700, 500));

        editor.Redact.ApplyCommand.Execute(null);
        harness.Pump();

        Assert.Equal(0xFFFFFFFF, Pixel(harness, 600, 400));
    });

    [Fact]
    public void Tiny_accidental_clicks_mark_nothing() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Redact;
        harness.Pump();

        Drag(harness, At(harness, 500, 300), At(harness, 500, 300));

        Assert.False(editor.Redact.HasPending);
    });
}
