using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PicKit.Core.Rendering;

namespace PicKit.UiTests;

public class NavigationUiTests
{
    private static void Zoom(UiHarness harness, double zoom)
    {
        var canvas = harness.Window.Canvas;
        canvas.View.ZoomAt(new SkiaSharp.SKPoint(100, 100), zoom);
        canvas.Redraw();
        harness.Pump();
    }

    private static void ScrollBarMoved(ScrollBar bar, double value)
    {
        bar.Value = value;
        bar.RaiseEvent(new ScrollEventArgs(ScrollEventType.ThumbTrack, value) { RoutedEvent = ScrollBar.ScrollEvent });
    }

    [Fact]
    public void Select_comes_first_and_is_what_an_image_opens_with() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1600, 1000);
        var editor = harness.ViewModel.Editor;

        Assert.Same(editor.Select, editor.EditTools[0]);
        Assert.Equal("Select", editor.EditTools[0].Name);
        Assert.Same(editor.Select, editor.ActiveTool);
    });

    [Fact]
    public void Select_shows_no_panel_and_leaves_the_image_alone() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1600, 1000);
        var editor = harness.ViewModel.Editor;

        Assert.False(editor.HasToolPanel);
        Assert.Equal(Visibility.Collapsed, harness.Window.ToolPanel.Visibility);
        Assert.Null(harness.Window.Canvas.Tool);

        editor.ActiveTool = editor.Crop;
        harness.Pump();
        Assert.True(editor.HasToolPanel);
    });

    [Fact]
    public void Dragging_with_select_moves_around_a_zoomed_image() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1600, 1000);
        Zoom(harness, 2);

        var canvas = harness.Window.Canvas;
        var before = canvas.View.OffsetX;
        Assert.True(canvas.PressAt(new Point(300, 300), MouseButton.Left, ModifierKeys.None));
        canvas.MoveTo(new Point(200, 300), ModifierKeys.None);
        canvas.ReleaseAt(new Point(200, 300), MouseButton.Left, ModifierKeys.None);

        Assert.True(canvas.View.OffsetX < before);
        Assert.False(harness.ViewModel.Editor.Document!.IsDirty);
    });

    [Fact]
    public void Escape_from_a_tool_goes_back_to_select() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1600, 1000);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Blur;
        harness.Pump();

        editor.CancelCommand.Execute(null);
        harness.Pump();

        Assert.Same(editor.Select, editor.ActiveTool);
    });

    [Fact]
    public void Scroll_bars_appear_only_when_the_image_is_bigger_than_the_view() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1600, 1000);
        var window = harness.Window;
        Assert.Equal(Visibility.Collapsed, window.HorizontalBar.Visibility);
        Assert.Equal(Visibility.Collapsed, window.VerticalBar.Visibility);

        Zoom(harness, 3);
        Assert.Equal(Visibility.Visible, window.HorizontalBar.Visibility);
        Assert.Equal(Visibility.Visible, window.VerticalBar.Visibility);
        harness.Screenshot("scroll-bars");

        window.Canvas.FitCommand.Execute(null);
        harness.Pump();
        Assert.Equal(Visibility.Collapsed, window.HorizontalBar.Visibility);
        Assert.Equal(Visibility.Collapsed, window.VerticalBar.Visibility);
    });

    [Fact]
    public void Moving_a_scroll_bar_moves_the_image_on_that_axis_only() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1600, 1000);
        Zoom(harness, 3);
        var window = harness.Window;
        var view = window.Canvas.View;
        var top = view.OffsetY;

        ScrollBarMoved(window.HorizontalBar, window.HorizontalBar.Maximum);
        harness.Pump();

        Assert.Equal(view.HorizontalScroll.Maximum, view.HorizontalScroll.Value, 1);
        Assert.Equal(top, view.OffsetY, 1);

        var left = view.OffsetX;
        ScrollBarMoved(window.VerticalBar, window.VerticalBar.Maximum / 2);
        harness.Pump();

        Assert.Equal(left, view.OffsetX, 1);
        Assert.Equal(view.VerticalScroll.Maximum / 2, view.VerticalScroll.Value, 1);
    });

    [Fact]
    public void Scroll_bars_follow_a_pan() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1600, 1000);
        Zoom(harness, 3);
        var window = harness.Window;
        var before = window.HorizontalBar.Value;

        var canvas = window.Canvas;
        canvas.PressAt(new Point(300, 300), MouseButton.Left, ModifierKeys.None);
        canvas.MoveTo(new Point(200, 300), ModifierKeys.None);
        canvas.ReleaseAt(new Point(200, 300), MouseButton.Left, ModifierKeys.None);
        harness.Pump();

        Assert.True(window.HorizontalBar.Value > before);
    });

    [Fact]
    public void Scroll_range_covers_the_whole_image_and_its_margin()
    {
        var view = new ViewTransform();
        view.SetViewport(800, 600);
        view.SetImage(1000, 1000, refit: false);
        view.ZoomAt(new SkiaSharp.SKPoint(0, 0), 2);

        var axis = view.HorizontalScroll;
        Assert.Equal(2000 + (2 * view.Padding) - 800, axis.Maximum, 3);

        view.ScrollTo(0, 0);
        Assert.Equal(view.Padding, view.OffsetX, 3);

        view.ScrollTo(axis.Maximum, 0);
        Assert.Equal(800 - 2000 - view.Padding, view.OffsetX, 3);

        view.ScrollTo(axis.Maximum * 5, 0);
        Assert.Equal(800 - 2000 - view.Padding, view.OffsetX, 3);
    }

    [Fact]
    public void A_view_that_fits_has_nothing_to_scroll()
    {
        var view = new ViewTransform();
        view.SetViewport(800, 600);
        view.SetImage(1000, 1000, refit: true);

        Assert.Equal(0, view.HorizontalScroll.Maximum);
        Assert.Equal(0, view.VerticalScroll.Maximum);
    }
}
