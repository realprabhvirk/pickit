using System.Windows;
using System.Windows.Input;
using PicKit.Controls.Tools;
using PicKit.Core.Markup;
using SkiaSharp;

namespace PicKit.UiTests;

public class MarkupUiTests
{
    private static void Drag(UiHarness harness, Point from, Point to, ModifierKeys modifiers = ModifierKeys.None)
    {
        var canvas = harness.Window.Canvas;
        Assert.True(canvas.PressAt(from, MouseButton.Left, modifiers));
        for (var step = 1; step <= 8; step++)
        {
            canvas.MoveTo(new Point(from.X + ((to.X - from.X) * step / 8), from.Y + ((to.Y - from.Y) * step / 8)), modifiers);
        }

        canvas.ReleaseAt(to, MouseButton.Left, modifiers);
        harness.Pump();
    }

    private static Point At(UiHarness harness, float x, float y) => TestFiles.ToElement(harness.Window.Canvas, new SKPoint(x, y));

    [Fact]
    public void Pen_draws_changes_colour_and_undoes() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        var markup = editor.Markup;
        editor.ActiveTool = markup;
        harness.Pump();
        Assert.Equal(MarkupMode.Pen, markup.Mode);

        var document = editor.Document!;
        var pixels = document.Pixels;
        Drag(harness, At(harness, 200, 200), At(harness, 500, 300));

        var first = Assert.IsType<StrokeElement>(Assert.Single(document.Markups));
        Assert.Same(pixels, document.Pixels);
        Assert.True(first.Points.Length > 3);

        markup.ChooseSwatchCommand.Execute(markup.Palette.First(choice => choice.Name == "Blue"));
        markup.Thickness = 12;
        Drag(harness, At(harness, 200, 500), At(harness, 500, 600));
        Assert.Equal(2, document.Markups.Length);
        Assert.Equal(new SKColor(0x00, 0x78, 0xD4), document.Markups[1].Style.Color);
        Assert.Equal(12 * 1.2f, document.Markups[1].Style.Thickness, 2f);
        harness.Screenshot("markup-pen");

        editor.UndoCommand.Execute(null);
        harness.Pump();
        Assert.Single(document.Markups);

        editor.RedoCommand.Execute(null);
        harness.Pump();
        Assert.Equal(2, document.Markups.Length);
    });

    [Theory]
    [InlineData(MarkupMode.Arrow, typeof(LineElement))]
    [InlineData(MarkupMode.Line, typeof(LineElement))]
    [InlineData(MarkupMode.Rectangle, typeof(ShapeElement))]
    [InlineData(MarkupMode.Ellipse, typeof(ShapeElement))]
    public void Shapes_are_added_by_dragging(MarkupMode mode, Type expected) => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Markup;
        editor.Markup.SelectedMode = editor.Markup.Modes.First(choice => choice.Mode == mode);
        harness.Pump();

        Drag(harness, At(harness, 300, 200), At(harness, 700, 500));

        Assert.IsType(expected, Assert.Single(editor.Document!.Markups));
    });

    [Fact]
    public void A_click_with_a_shape_tool_adds_nothing() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Markup;
        editor.Markup.SelectedMode = editor.Markup.Modes.First(choice => choice.Mode == MarkupMode.Rectangle);
        harness.Pump();

        Drag(harness, At(harness, 300, 200), At(harness, 300, 200));

        Assert.Empty(editor.Document!.Markups);
        Assert.False(editor.Document.History.CanUndo);
    });

    [Fact]
    public void Shift_makes_perfect_squares() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Markup;
        editor.Markup.SelectedMode = editor.Markup.Modes.First(choice => choice.Mode == MarkupMode.Rectangle);
        harness.Pump();

        Drag(harness, At(harness, 300, 200), At(harness, 700, 300), ModifierKeys.Shift);

        var shape = Assert.IsType<ShapeElement>(Assert.Single(editor.Document!.Markups));
        Assert.Equal(shape.Box.Width, shape.Box.Height, 1f);
    });

    [Fact]
    public void Select_moves_as_one_undo_step_and_delete_removes() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        var markup = editor.Markup;
        editor.ActiveTool = markup;
        markup.SelectedMode = markup.Modes.First(choice => choice.Mode == MarkupMode.Rectangle);
        harness.Pump();
        Drag(harness, At(harness, 300, 200), At(harness, 600, 400));
        var document = editor.Document!;
        var original = (ShapeElement)document.Markups[0];

        markup.SelectedMode = markup.Modes.First(choice => choice.Mode == MarkupMode.Select);
        harness.Pump();
        Drag(harness, At(harness, 300, 300), At(harness, 400, 350));

        var moved = (ShapeElement)document.Markups[0];
        Assert.Equal(original.Box.Left + 100, moved.Box.Left, 3f);
        Assert.Equal(original.Box.Top + 50, moved.Box.Top, 3f);
        Assert.NotNull(markup.Selected);
        harness.Screenshot("markup-selected");

        editor.UndoCommand.Execute(null);
        harness.Pump();
        Assert.Equal(original.Box.Left, ((ShapeElement)document.Markups[0]).Box.Left, 0.1f);

        editor.RedoCommand.Execute(null);
        harness.Pump();
        Drag(harness, At(harness, 400, 350), At(harness, 400, 350));
        Assert.NotNull(markup.Selected);

        editor.DeleteSelectionCommand.Execute(null);
        harness.Pump();
        Assert.Empty(document.Markups);
    });

    [Fact]
    public void Text_tool_places_edits_and_removes_text() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Text;
        harness.Pump();

        Drag(harness, At(harness, 300, 300), At(harness, 300, 300));
        Assert.NotNull(editor.Text.TextEdit);
        var box = harness.Window.TextEntry;
        Assert.Equal(Visibility.Visible, box.Visibility);
        box.Text = "Hello PicKit";
        harness.Screenshot("text-typing");

        editor.Text.CommitTextEdit(box.Text);
        harness.Pump();
        var document = editor.Document!;
        var text = Assert.IsType<TextElement>(Assert.Single(document.Markups));
        Assert.Equal("Hello PicKit", text.Text);
        Assert.Equal(300, text.Position.X, 3f);
        harness.Screenshot("text-placed");

        // Clicking the same text again edits it instead of adding another.
        Drag(harness, At(harness, 320, 310), At(harness, 320, 310));
        Assert.Same(text, editor.Text.TextEdit?.Existing);

        editor.Text.CommitTextEdit(string.Empty);
        harness.Pump();
        Assert.Empty(document.Markups);
    });

    [Fact]
    public void Empty_text_adds_nothing() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Text;
        harness.Pump();

        Drag(harness, At(harness, 300, 300), At(harness, 300, 300));
        editor.Text.CommitTextEdit("   ");
        harness.Pump();

        Assert.Empty(editor.Document!.Markups);
    });

    [Fact]
    public void Highlight_defaults_to_translucent_yellow() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Highlight;
        harness.Pump();

        Drag(harness, At(harness, 200, 400), At(harness, 900, 420));

        var stroke = Assert.IsType<StrokeElement>(Assert.Single(editor.Document!.Markups));
        Assert.True(stroke.IsHighlighter);
        Assert.Equal(0xFF, stroke.Style.Color.Red);
        Assert.True(stroke.Style.Opacity < 1);
        harness.Screenshot("highlight");
    });

    [Fact]
    public void Markup_stays_in_place_through_a_crop() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Markup;
        editor.Markup.SelectedMode = editor.Markup.Modes.First(choice => choice.Mode == MarkupMode.Rectangle);
        harness.Pump();
        Drag(harness, At(harness, 300, 300), At(harness, 500, 400));
        var box = ((ShapeElement)editor.Document!.Markups[0]).Box;

        editor.ActiveTool = editor.Crop;
        harness.Pump();
        var tool = (CropCanvasTool)editor.Crop.CanvasTool;
        var corner = TestFiles.ToElement(harness.Window.Canvas, new SKPoint(tool.Box.Left, tool.Box.Top));
        Drag(harness, corner, At(harness, 100, 100));
        var crop = tool.PixelBox;

        await editor.Crop.ApplyCommand.ExecuteAsync(null);
        harness.Pump();

        var after = ((ShapeElement)editor.Document.Markups[0]).Box;
        Assert.Equal(box.Left - crop.Left, after.Left, 1f);
        Assert.Equal(box.Top - crop.Top, after.Top, 1f);
    });
}
