using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using PicKit.Background;
using PicKit.Core.Background;
using PicKit.Core.Imaging;
using SkiaSharp;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace PicKit.UiTests;

/// <summary>The helper's parts, tested without ever starting it or showing its windows.</summary>
public class BackgroundPartsTests
{
    [Theory]
    [InlineData(1000, 1002, SwipeOutcome.Click)]
    [InlineData(1000, 1006, SwipeOutcome.Click)]
    [InlineData(1000, 1050, SwipeOutcome.SnapBack)]
    [InlineData(1000, 1200, SwipeOutcome.Dismiss)]
    [InlineData(1000, 800, SwipeOutcome.Dismiss)]
    public void A_click_is_told_from_a_swipe(int from, int to, SwipeOutcome expected)
    {
        var swipe = new SwipeTracker(340);
        swipe.Press(from);
        swipe.Move(to);

        Assert.Equal(expected, swipe.Release());
    }

    [Fact]
    public void Wandering_off_and_coming_back_is_not_a_click()
    {
        var swipe = new SwipeTracker(340);
        swipe.Press(1000);
        swipe.Move(1100);
        swipe.Move(1000);

        Assert.Equal(SwipeOutcome.SnapBack, swipe.Release());
    }

    [Fact]
    public void The_window_follows_the_pointer_while_swiping()
    {
        var swipe = new SwipeTracker(340);
        swipe.Press(1000);

        Assert.Equal(120, swipe.Move(1120));
        Assert.Equal(-40, swipe.Move(960));
    }

    [Fact]
    public void The_notification_draws_as_a_picture()
    {
        using var thumbnail = ImageBuffer.Create(160, 100);
        thumbnail.Bitmap.Erase(SKColors.SteelBlue);
        const int scale = 2;
        var (width, height) = (NotificationWindow.WidthDip * scale, NotificationWindow.HeightDip * scale);

        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            NotificationWindow.Paint(canvas, width, height, scale, "Screenshot taken", "Click to save and open.\nSwipe away to decide later.", thumbnail);
        }

        Save(bitmap, "notification.png");
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
        Assert.Equal(SKColors.SteelBlue, bitmap.GetPixel(60, height / 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_drop_box_draws_as_a_picture(bool isHighlighted)
    {
        const int scale = 3;
        var size = DropBoxWindow.SizeDip * scale;

        using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            DropBoxWindow.Paint(canvas, size, scale, isHighlighted);
        }

        Save(bitmap, isHighlighted ? "dropbox-hover.png" : "dropbox.png");
        Assert.Equal(0, bitmap.GetPixel(0, 0).Alpha);
    }

    [Fact]
    public void The_drop_target_is_reachable_from_windows_as_the_real_interface()
    {
        var target = new DropTarget(_ => { }, _ => { });
        var iid = new Guid("00000122-0000-0000-C000-000000000046");
        var unknown = Marshal.GetIUnknownForObject(target);
        try
        {
            Assert.Equal(0, Marshal.QueryInterface(unknown, in iid, out var pointer));
            Marshal.Release(pointer);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    [Fact]
    public void Dragging_over_highlights_and_leaving_clears_it()
    {
        var states = new List<bool>();
        var target = new DropTarget(states.Add, _ => { });
        var effect = 1;

        target.DragEnter(new DataObject("text"), 0, 0, ref effect);
        target.DragLeave();

        Assert.Equal([true, false], states);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 1)]
    [InlineData(4, 4)]
    [InlineData(2, 0)]
    public void A_drag_is_taken_as_a_copy_or_failing_that_a_link(int offered, int expected)
    {
        var target = new DropTarget(_ => { }, _ => { });
        var effect = offered;

        target.DragOver(0, 0, ref effect);

        Assert.Equal(expected, effect);
    }

    [Fact]
    public void Files_dragged_from_explorer_arrive_as_paths()
    {
        var data = new DataObject(DataFormats.FileDrop, new[] { @"C:\Photos\one.png", @"C:\Photos\two.jpg" });

        var items = DropReader.Read(data);

        Assert.Equal([@"C:\Photos\one.png", @"C:\Photos\two.jpg"], items.Paths);
    }

    [Fact]
    public void An_image_a_browser_carries_as_a_virtual_file_arrives_as_bytes()
    {
        var descriptor = new byte[4 + 592];
        descriptor[0] = 1;
        Encoding.Unicode.GetBytes("cat.png").CopyTo(descriptor, 4 + 72);
        byte[] contents = [9, 8, 7, 6];

        var data = new DataObject();
        data.SetData("FileGroupDescriptorW", new MemoryStream(descriptor));
        data.SetData("FileContents", new MemoryStream(contents));

        var items = DropReader.Read(data);

        Assert.Equal("cat.png", items.File!.Name);
        Assert.Equal(contents, items.File.Data.Take(contents.Length));
    }

    [Fact]
    public void The_image_in_a_dragged_link_wins_over_the_page_it_links_to()
    {
        var html = "Version:0.9\r\nStartHTML:1\r\n<html><body><a href=\"https://a.example/page\"><img src=\"https://a.example/cat.png\"></a></body></html>";
        var url = Encoding.Unicode.GetBytes("https://a.example/page\0");

        var data = new DataObject();
        data.SetData("HTML Format", new MemoryStream(Encoding.UTF8.GetBytes(html)));
        data.SetData("UniformResourceLocatorW", new MemoryStream(url));

        Assert.Equal(new Uri("https://a.example/cat.png"), DropReader.Read(data).Url);
    }

    [Fact]
    public void A_plain_web_address_arrives_as_a_download()
    {
        var data = new DataObject();
        data.SetData("UniformResourceLocatorW", new MemoryStream(Encoding.Unicode.GetBytes("https://a.example/dog.jpg\0")));

        Assert.Equal(new Uri("https://a.example/dog.jpg"), DropReader.Read(data).Url);
    }

    [Fact]
    public void Dragged_text_is_not_mistaken_for_anything()
    {
        var items = DropReader.Read(new DataObject(DataFormats.UnicodeText, "just some words"));

        Assert.Empty(items.Paths);
        Assert.Null(items.File);
        Assert.Null(items.Url);
    }

    private static void Save(SKBitmap bitmap, string name)
    {
        var folder = Environment.GetEnvironmentVariable("PICKIT_SHOTS") ?? Path.GetTempPath();
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(folder, name), data.ToArray());
    }
}
