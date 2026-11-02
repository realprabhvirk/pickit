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
    [Fact]
    public void Windows_accepts_the_notification_we_build()
    {
        var link = ScreenshotLink.For("Screenshot 2026-11-02 180312.png");
        var xml = ToastContent.Build("Screenshot taken", "It's on your clipboard. Click to save & open it.", @"C:\Temp\a b.png", link);

        // Built by Windows itself but never shown, so nothing appears on screen.
        Assert.NotNull(WindowsToast.Create(xml));
    }

    [Fact]
    public void Windows_turns_down_a_broken_notification() =>
        Assert.Throws<COMException>(() => WindowsToast.Create("<toast><not closed"));

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

    [Fact]
    public void A_screenshot_is_stored_exactly_and_is_ready_for_the_clipboard()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PicKitTests", Guid.NewGuid().ToString("N"));
        var inbox = new ImageInbox(folder);
        using var screen = ImageBuffer.Create(1920, 1200);
        screen.Bitmap.Erase(SKColors.Teal);

        var (path, items) = BackgroundAgent.Store(inbox, screen, new DateTime(2026, 11, 2, 18, 3, 12));

        Assert.Equal("Screenshot 2026-11-02 180312.png", Path.GetFileName(path));
        Assert.Contains(items, item => item.Format == ClipboardCodec.PngFormat);
        Assert.Contains(items, item => item.Format == ClipboardCodec.DibFormat);

        using (var stored = ImageDecoder.Decode(path))
        {
            Assert.Equal(ImageFormat.Png, stored.Format);
            Assert.True(screen.Pixels.SequenceEqual(stored.Pixels.Pixels));
        }

        using (var fromClipboard = ClipboardCodec.Decode(format => items.FirstOrDefault(item => item.Format == format)?.Data))
        {
            Assert.True(screen.Pixels.SequenceEqual(fromClipboard!.Pixels));
        }

        Directory.Delete(folder, recursive: true);
    }

    [Fact]
    public void The_real_screen_can_be_read_without_anything_being_saved_or_shown()
    {
        try
        {
            // Held in memory for this check alone, and never written anywhere.
            var screen = ScreenCapture.Freeze();
            using var image = screen.Image;

            Assert.True(image.Width >= 640 && image.Height >= 480);
            Assert.False(image.HasTransparency());
            Assert.True(screen.Monitor.Width > 0 && screen.Monitor.Right <= image.Width && screen.Monitor.Bottom <= image.Height);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // A locked PC has no screen to read, which the helper reports instead of crashing.
        }
    }

    private static void Save(SKBitmap bitmap, string name)
    {
        var folder = Environment.GetEnvironmentVariable("PICKIT_SHOTS") ?? Path.GetTempPath();
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(folder, name), data.ToArray());
    }
}
