using System.IO;
using PicKit.Background;
using PicKit.Core.Imaging;
using PicKit.ViewModels.Tools;
using SkiaSharp;

namespace PicKit.UiTests;

public class KeyChordWatcherTests
{
    private const int Control = 0x11;
    private const int Alt = 0x12;
    private const int Shift = 0x10;
    private const int LeftWindows = 0x5B;
    private const int Letter = 'S';

    private static bool PressInOrder(KeyChordWatcher keys, long now, params int[] held)
    {
        foreach (var key in held)
        {
            keys.OnKey(key, true, now);
        }

        return keys.OnKey(Letter, true, now);
    }

    [Fact]
    public void Ctrl_alt_s_is_recognised_in_either_order()
    {
        Assert.True(PressInOrder(new KeyChordWatcher(), 1000, Control, Alt));
        Assert.True(PressInOrder(new KeyChordWatcher(), 1000, Alt, Control));
    }

    [Fact]
    public void Plain_typing_never_counts()
    {
        var keys = new KeyChordWatcher();

        Assert.False(keys.OnKey(Letter, true, 1000));
        keys.OnKey(Letter, false, 1010);
        Assert.False(PressInOrder(new KeyChordWatcher(), 1000, Control));
        Assert.False(PressInOrder(new KeyChordWatcher(), 1000, Alt));
    }

    [Theory]
    [InlineData(Shift)]
    [InlineData(LeftWindows)]
    public void Other_combinations_are_left_alone(int extra) =>
        Assert.False(PressInOrder(new KeyChordWatcher(), 1000, Control, Alt, extra));

    [Fact]
    public void Holding_the_keys_down_fires_only_once()
    {
        var keys = new KeyChordWatcher();
        Assert.True(PressInOrder(keys, 1000, Control, Alt));

        Assert.False(keys.OnKey(Letter, true, 1030));
        Assert.False(keys.OnKey(Letter, true, 1060));

        keys.OnKey(Letter, false, 1100);
        Assert.True(keys.OnKey(Letter, true, 1200));
    }

    [Fact]
    public void Letting_go_of_a_key_ends_the_combination()
    {
        var keys = new KeyChordWatcher();
        keys.OnKey(Control, true, 1000);
        keys.OnKey(Alt, true, 1000);
        keys.OnKey(Alt, false, 1100);

        Assert.False(keys.OnKey(Letter, true, 1200));
    }

    [Fact]
    public void Keys_released_where_the_helper_cannot_see_stop_counting_as_held()
    {
        var keys = new KeyChordWatcher();
        keys.OnKey(Control, true, 1000);
        keys.OnKey(Alt, true, 1000);

        // Ctrl+Alt+Del: the release happens on the sign-in screen, and later typing must be safe.
        Assert.False(keys.OnKey(Letter, true, 60_000));
    }

    [Fact]
    public void Slowly_reaching_for_the_letter_still_works_while_the_keys_repeat()
    {
        var keys = new KeyChordWatcher();
        keys.OnKey(Control, true, 1000);
        keys.OnKey(Alt, true, 1100);
        for (var time = 1200; time < 9000; time += 500)
        {
            keys.OnKey(Alt, true, time);
        }

        Assert.True(keys.OnKey(Letter, true, 9000));
    }

    [Fact]
    public void One_press_arriving_by_two_routes_starts_one_snip()
    {
        var gate = new TriggerGate();

        Assert.True(gate.TryPass(1000));
        Assert.False(gate.TryPass(1005));
        Assert.True(gate.TryPass(2000));
    }
}

public class SnipLayoutTests
{
    private static readonly SnipLayout Layout = new(new SKRectI(0, 0, 1920, 1200), new SKRectI(0, 0, 1920, 1200), 1);

    [Fact]
    public void The_selection_is_the_same_whichever_way_it_is_dragged()
    {
        var forward = Layout.Selection(new SKPointI(100, 200), new SKPointI(400, 500));
        var backward = Layout.Selection(new SKPointI(400, 500), new SKPointI(100, 200));

        Assert.Equal(new SKRectI(100, 200, 400, 500), forward);
        Assert.Equal(forward, backward);
    }

    [Fact]
    public void A_drag_past_the_edge_stops_at_the_screen() =>
        Assert.Equal(new SKRectI(1800, 1100, 1920, 1200), Layout.Selection(new SKPointI(1800, 1100), new SKPointI(3000, 3000)));

    [Theory]
    [InlineData(3, 100, false)]
    [InlineData(100, 3, false)]
    [InlineData(4, 4, true)]
    [InlineData(200, 100, true)]
    public void A_tiny_drag_is_not_a_selection(int width, int height, bool expected) =>
        Assert.Equal(expected, SnipLayout.IsUsable(new SKRectI(10, 10, 10 + width, 10 + height)));

    [Fact]
    public void The_bar_is_centred_near_the_top_of_the_monitor_in_use()
    {
        var layout = new SnipLayout(new SKRectI(0, 0, 3840, 1200), new SKRectI(1920, 0, 3840, 1200), 1);

        Assert.Equal(2880f, layout.Bar.MidX, 1f);
        Assert.True(layout.Bar.Top < 40);
    }

    [Fact]
    public void Buttons_are_found_and_nothing_else_is()
    {
        Assert.Equal(SnipButton.FullScreen, Layout.ButtonAt(new SKPointI(Layout.FullScreen.MidX, Layout.FullScreen.MidY)));
        Assert.Equal(SnipButton.Cancel, Layout.ButtonAt(new SKPointI(Layout.Cancel.MidX, Layout.Cancel.MidY)));
        Assert.Equal(SnipButton.None, Layout.ButtonAt(new SKPointI(500, 500)));
        Assert.True(Layout.IsOnBar(new SKPointI(Layout.Bar.Left + 2, Layout.Bar.MidY)));
        Assert.False(Layout.IsOnBar(new SKPointI(500, 500)));
    }

    [Fact]
    public void The_snipping_screen_draws_as_a_picture()
    {
        using var photo = ImageBuffer.Create(1280, 800);
        photo.Bitmap.Erase(SKColors.SteelBlue);
        using var dimmed = SnipOverlay.Dimmed(photo);

        using var bright = photo.AsImage();
        using var dim = dimmed.AsImage();
        var layout = new SnipLayout(new SKRectI(0, 0, 1280, 800), new SKRectI(0, 0, 1280, 800), 1);
        var selection = new SKRectI(300, 220, 900, 620);

        using var bitmap = new SKBitmap(new SKImageInfo(1280, 800, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            SnipOverlay.Paint(canvas, layout, bright, dim, selection, SnipButton.FullScreen);
        }

        TestPictures.Save(bitmap, "snip-overlay.png");
        Assert.Equal(SKColors.SteelBlue, bitmap.GetPixel(600, 400));

        // Outside the snip it's the same picture, only darker. It must never lose its colour or
        // turn see-through, because the window drawing this ignores alpha and would show black.
        var outside = bitmap.GetPixel(100, 400);
        Assert.Equal(255, outside.Alpha);
        Assert.InRange(outside.Blue, 60, 120);
        Assert.True(outside.Blue > outside.Red);
    }
}

public class RibbonLayoutTests
{
    private static readonly SKRectI Work = new(0, 0, 1920, 1152);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_strip_sits_on_the_screen_edge_inside_the_window(bool isOnLeft)
    {
        var layout = RibbonLayout.Compute(Work, 1, isOnLeft, 0.6);

        Assert.Equal(isOnLeft ? 0 : Work.Right, isOnLeft ? layout.Window.Left : layout.Window.Right);
        Assert.Equal(isOnLeft ? 0 : layout.Window.Width, isOnLeft ? layout.Strip.Left : layout.Strip.Right);
        Assert.True(layout.Strip.Top >= 0 && layout.Strip.Bottom <= layout.Window.Height);
    }

    [Fact]
    public void Closed_it_is_thin_like_filedock()
    {
        var layout = RibbonLayout.Compute(Work, 1, false, 0.5);

        Assert.Equal(10, layout.Strip.Width);
        Assert.Equal(64, layout.Strip.Height);
        Assert.Equal(4, layout.Grip.Width);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(1.5f)]
    [InlineData(2f)]
    public void It_scales_with_the_display(float scale)
    {
        var layout = RibbonLayout.Compute(Work, scale, false, 0.5);

        Assert.Equal((int)Math.Round(10 * scale), layout.Strip.Width);
        Assert.Equal((int)Math.Round(204 * scale), layout.Window.Width);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(double.NaN)]
    public void The_window_and_strip_always_stay_on_screen(double position)
    {
        var layout = RibbonLayout.Compute(Work, 1, false, position);

        Assert.True(layout.Window.Top >= Work.Top && layout.Window.Bottom <= Work.Bottom);
        Assert.True(layout.Window.Top + layout.Strip.Top >= Work.Top);
        Assert.True(layout.Window.Top + layout.Strip.Bottom <= Work.Bottom);
    }

    [Fact]
    public void The_panel_opens_around_the_strip_so_it_never_moves_from_under_the_pointer()
    {
        foreach (var position in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            var layout = RibbonLayout.Compute(Work, 1, false, position);

            Assert.True(layout.Strip.Top >= 0 && layout.Strip.Bottom <= layout.Window.Height);
        }
    }

    [Fact]
    public void The_position_moves_the_strip_down_the_edge()
    {
        var high = RibbonLayout.Compute(Work, 1, false, 0.1);
        var low = RibbonLayout.Compute(Work, 1, false, 0.9);

        Assert.True(low.Window.Top + low.Strip.Top > high.Window.Top + high.Strip.Top);
    }

    [Fact]
    public void Buttons_are_found_and_the_drop_zone_is_not_one()
    {
        var layout = RibbonLayout.Compute(Work, 1, false, 0.5);

        Assert.Equal(RibbonPart.Snip, layout.PartAt(new SKPointI(layout.Snip.MidX, layout.Snip.MidY)));
        Assert.Equal(RibbonPart.Open, layout.PartAt(new SKPointI(layout.Open.MidX, layout.Open.MidY)));
        Assert.Equal(RibbonPart.None, layout.PartAt(new SKPointI(layout.DropZone.MidX, layout.DropZone.MidY)));
    }

    [Fact]
    public void Closed_only_the_thin_strip_exists_and_the_rest_lets_clicks_through()
    {
        const int scale = 2;
        var layout = RibbonLayout.Compute(new SKRectI(0, 0, 3840, 2304), scale, false, 0.5);
        using var bitmap = Draw(layout, isExpanded: false, isDragOver: false);
        TestPictures.Save(bitmap, "ribbon-closed.png");

        // Fully transparent pixels are what let clicks pass through to the app beneath.
        Assert.Equal(0, bitmap.GetPixel(layout.DropZone.MidX, layout.DropZone.MidY).Alpha);
        Assert.Equal(0, bitmap.GetPixel(layout.Strip.Left - 2, layout.Strip.MidY).Alpha);

        // Invisible, but enough to catch the pointer a little wider than the bar it can see.
        Assert.Equal(1, bitmap.GetPixel(layout.Strip.Left + 1, layout.Strip.MidY).Alpha);
        Assert.True(bitmap.GetPixel(layout.Grip.MidX, layout.Grip.MidY).Alpha > 100);
    }

    [Theory]
    [InlineData(false, "ribbon-open.png")]
    [InlineData(true, "ribbon-drag.png")]
    public void Open_it_draws_the_panel(bool isDragOver, string name)
    {
        const int scale = 2;
        var layout = RibbonLayout.Compute(new SKRectI(0, 0, 3840, 2304), scale, false, 0.5);
        using var bitmap = Draw(layout, isExpanded: true, isDragOver);

        TestPictures.Save(bitmap, name);
        Assert.Equal(255, bitmap.GetPixel(layout.Snip.MidX, layout.Snip.Top + 2).Alpha);
    }

    private static SKBitmap Draw(RibbonLayout layout, bool isExpanded, bool isDragOver)
    {
        var bitmap = new SKBitmap(new SKImageInfo(layout.Window.Width, layout.Window.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        EdgeRibbonWindow.Paint(canvas, layout, isExpanded, isDragOver, RibbonPart.Snip);
        return bitmap;
    }
}

internal static class TestPictures
{
    public static void Save(SKBitmap bitmap, string name)
    {
        var folder = Environment.GetEnvironmentVariable("PICKIT_SHOTS") ?? Path.GetTempPath();
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(folder, name), data.ToArray());
    }
}
