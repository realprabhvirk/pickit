using System.Text;
using Microsoft.Win32;
using PicKit.Core.Background;
using PicKit.Core.Imaging;
using PicKit.Core.Shell;

namespace PicKit.Tests;

public class ImageInboxTests
{
    private static byte[] Png()
    {
        using var image = TestImages.Solid(32, 32, SkiaSharp.SKColors.Teal);
        return ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png));
    }

    [Fact]
    public void Added_images_wait_newest_first()
    {
        var inbox = new ImageInbox(TestImages.TempFolder());

        var first = inbox.Add(Png(), "one.png");
        File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddMinutes(-5));
        var second = inbox.Add(Png(), "two.png");

        Assert.Equal([second, first], inbox.Waiting);
    }

    [Fact]
    public void Same_named_images_never_replace_each_other()
    {
        var inbox = new ImageInbox(TestImages.TempFolder());

        var first = inbox.Add(Png(), "shot.png");
        var second = inbox.Add(Png(), "shot.png");

        Assert.NotEqual(first, second);
        Assert.Equal(2, inbox.Waiting.Count);
    }

    [Fact]
    public void A_missing_inbox_is_simply_empty() =>
        Assert.Empty(new ImageInbox(Path.Combine(TestImages.TempFolder(), "not-here")).Waiting);

    [Fact]
    public void Screenshot_names_sort_by_time_and_are_valid_file_names()
    {
        var name = ImageInbox.ScreenshotName(new DateTime(2026, 11, 2, 18, 3, 12));

        Assert.Equal("Screenshot 2026-11-02 180312.png", name);
        Assert.Equal(name, PicKit.Core.Files.FileNames.Sanitize(name));
    }

    [Fact]
    public void Keeping_moves_the_image_out_of_the_inbox()
    {
        var inbox = new ImageInbox(TestImages.TempFolder());
        var target = Path.Combine(TestImages.TempFolder(), "new folder");
        var path = inbox.Add(Png(), "shot.png");

        var kept = inbox.Keep(path, target);

        Assert.Equal(Path.Combine(target, "shot.png"), kept);
        Assert.True(File.Exists(kept));
        Assert.Empty(inbox.Waiting);
    }

    [Fact]
    public void Keeping_never_overwrites_what_is_already_there()
    {
        var inbox = new ImageInbox(TestImages.TempFolder());
        var target = TestImages.TempFolder();
        var existing = Path.Combine(target, "shot.png");
        File.WriteAllBytes(existing, [1, 2, 3]);

        var kept = inbox.Keep(inbox.Add(Png(), "shot.png"), target);

        Assert.NotEqual(existing, kept);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(existing));
    }

    [Fact]
    public void Contains_recognises_only_its_own_folder()
    {
        var inbox = new ImageInbox(TestImages.TempFolder());
        var path = inbox.Add(Png(), "shot.png");

        Assert.True(inbox.Contains(path));
        Assert.False(inbox.Contains(Path.Combine(TestImages.TempFolder(), "shot.png")));
        Assert.False(inbox.Contains(null));
    }

    [Fact]
    public void Trash_uses_what_it_was_given()
    {
        var trashed = new List<string>();
        var inbox = new ImageInbox(TestImages.TempFolder(), trashed.Add);
        var path = inbox.Add(Png(), "shot.png");

        inbox.Trash(path);

        Assert.Equal([path], trashed);
    }

    [Theory]
    [InlineData("photo.jpg", "photo.png")]
    [InlineData("photo", "photo.png")]
    [InlineData("..\\..\\evil.exe", "evil.png")]
    [InlineData("", "Dropped image.png")]
    public void Dropped_files_are_named_by_what_their_bytes_really_are(string name, string expected)
    {
        var inbox = new ImageInbox(TestImages.TempFolder());

        var path = inbox.AddDropped(new DroppedFile(name, Png()));

        Assert.EndsWith(expected, path);
        Assert.Equal(inbox.Folder, Path.GetDirectoryName(path));
    }

    [Fact]
    public void A_dropped_file_that_is_not_an_image_is_refused()
    {
        var inbox = new ImageInbox(TestImages.TempFolder());

        Assert.Null(inbox.AddDropped(new DroppedFile("photo.jpg", Encoding.UTF8.GetBytes("<html>not an image</html>"))));
        Assert.Empty(inbox.Waiting);
    }
}

public class ScreenshotNotificationTests
{
    private static byte[] Png()
    {
        using var image = TestImages.Solid(32, 32, SkiaSharp.SKColors.Teal);
        return ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png));
    }

    [Fact]
    public void A_link_carries_the_name_there_and_back()
    {
        const string name = "Screenshot 2026-11-02 180312.png";

        Assert.Equal(name, ScreenshotLink.NameIn(ScreenshotLink.For(name)));
    }

    [Theory]
    [InlineData("pickit:screenshot?name=..%5C..%5Csecrets.png")]
    [InlineData("pickit:screenshot?name=..%2Fsecrets.png")]
    [InlineData("pickit:screenshot?name=C%3A%5CWindows%5Cwin.png")]
    [InlineData("pickit:screenshot?name=evil.exe")]
    [InlineData("pickit:screenshot?name=")]
    [InlineData("pickit:format?drive=c")]
    [InlineData("https://example.com/a.png")]
    [InlineData("")]
    public void A_link_a_web_page_made_up_gets_nowhere(string link) => Assert.Null(ScreenshotLink.NameIn(link));

    [Fact]
    public void Clicking_saves_a_waiting_screenshot_then_opens_it()
    {
        var inbox = new ImageInbox(TestImages.TempFolder());
        var folder = TestImages.TempFolder();
        var path = inbox.Add(Png(), "shot.png");

        var request = ScreenshotLink.Resolve(ScreenshotLink.For("shot.png"), inbox, folder);

        Assert.Equal(ShellAction.Open, request.Action);
        Assert.Equal([Path.Combine(folder, "shot.png")], request.Paths);
        Assert.True(File.Exists(request.Paths[0]));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Clicking_again_opens_the_copy_already_saved()
    {
        var inbox = new ImageInbox(TestImages.TempFolder());
        var folder = TestImages.TempFolder();
        File.WriteAllBytes(Path.Combine(folder, "shot.png"), Png());

        var request = ScreenshotLink.Resolve(ScreenshotLink.For("shot.png"), inbox, folder);

        Assert.Equal([Path.Combine(folder, "shot.png")], request.Paths);
    }

    [Fact]
    public void Clicking_one_that_has_gone_just_opens_the_app()
    {
        var request = ScreenshotLink.Resolve(ScreenshotLink.For("shot.png"), new ImageInbox(TestImages.TempFolder()), TestImages.TempFolder());

        Assert.Equal(ShellAction.Open, request.Action);
        Assert.Empty(request.Paths);
    }

    [Fact]
    public void A_bad_link_opens_nothing_but_the_app()
    {
        var folder = TestImages.TempFolder();
        File.WriteAllBytes(Path.Combine(folder, "private.png"), Png());

        var request = ScreenshotLink.Resolve("pickit:screenshot?name=..%5Cprivate.png", new ImageInbox(TestImages.TempFolder()), folder);

        Assert.Empty(request.Paths);
    }

    [Fact]
    public void A_notification_is_silent_and_carries_its_link_and_picture()
    {
        var xml = ToastContent.Build("Screenshot taken", "Click it", @"C:\Temp\a b.png", "pickit:screenshot?name=a.png");

        Assert.Contains("<audio silent=\"true\"/>", xml);
        Assert.Contains("activationType=\"protocol\"", xml);
        Assert.Contains("launch=\"pickit:screenshot?name=a.png\"", xml);
        Assert.Contains("file:///C:/Temp/a%20b.png", xml);
    }

    [Fact]
    public void Text_cannot_break_out_of_the_notification()
    {
        var xml = ToastContent.Build("<b>", "a & \"b\"");

        Assert.Contains("&lt;b&gt;", xml);
        Assert.Contains("&amp;", xml);
        Assert.DoesNotContain("<b>", xml);
    }

    [Theory]
    [InlineData("--notification", ShellAction.Notification)]
    public void The_notification_switch_is_recognised(string argument, ShellAction expected) =>
        Assert.Equal(expected, LaunchRequest.Parse([argument, "--", "pickit:screenshot?name=a.png"]).Action);

    [Fact]
    public void A_link_cannot_pass_itself_off_as_a_switch()
    {
        var request = LaunchRequest.Parse(["--notification", "--", "--unregister"]);

        Assert.Equal(ShellAction.Notification, request.Action);
        Assert.Equal(["--unregister"], request.Paths);
    }
}

public class ImageSnifferTests
{
    [Theory]
    [InlineData(ImageFormat.Png, ".png")]
    [InlineData(ImageFormat.Jpeg, ".jpg")]
    [InlineData(ImageFormat.WebP, ".webp")]
    public void Real_encoded_images_are_recognised(ImageFormat format, string extension)
    {
        using var image = TestImages.Photo(32, 32);

        Assert.Equal(extension, ImageSniffer.ExtensionFor(ImageEncoder.Encode(image, new EncodeOptions(format))));
    }

    [Fact]
    public void Too_little_data_or_other_files_are_not_images()
    {
        Assert.Null(ImageSniffer.ExtensionFor([1, 2, 3]));
        Assert.Null(ImageSniffer.ExtensionFor(new byte[64]));
        Assert.Null(ImageSniffer.ExtensionFor(Encoding.ASCII.GetBytes("<!doctype html><html>")));
    }
}

public class DropReaderTests
{
    [Fact]
    public void A_wide_file_list_is_read()
    {
        var names = Encoding.Unicode.GetBytes("C:\\a b\\one.png\0C:\\two.jpg\0\0");
        var block = new byte[20 + names.Length];
        BitConverter.TryWriteBytes(block, 20);
        BitConverter.TryWriteBytes(block.AsSpan(16), 1);
        names.CopyTo(block, 20);

        Assert.Equal(["C:\\a b\\one.png", "C:\\two.jpg"], DropReader.ParseFileList(block));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(19)]
    [InlineData(4000)]
    public void A_damaged_file_list_gives_nothing(int offset)
    {
        var block = new byte[64];
        BitConverter.TryWriteBytes(block, offset);
        BitConverter.TryWriteBytes(block.AsSpan(16), 1);

        Assert.Empty(DropReader.ParseFileList(block));
    }

    [Fact]
    public void A_descriptor_gives_the_first_file_name()
    {
        var block = new byte[4 + 592];
        block[0] = 1;
        Encoding.Unicode.GetBytes("cat.webp").CopyTo(block, 4 + 72);

        Assert.Equal("cat.webp", DropReader.ParseDescriptorName(block));
    }

    [Fact]
    public void A_short_descriptor_gives_nothing() => Assert.Null(DropReader.ParseDescriptorName(new byte[8]));

    [Theory]
    [InlineData("<div><img src=\"https://a.example/cat.png\" alt=\"x\"></div>", "https://a.example/cat.png")]
    [InlineData("<IMG class='x' SRC='https://a.example/dog.jpg'>", "https://a.example/dog.jpg")]
    [InlineData("<img src=\"https://a.example/a.png?w=1&amp;h=2\">", "https://a.example/a.png?w=1&h=2")]
    [InlineData("<p>no picture here</p>", null)]
    public void An_image_address_is_found_in_a_page_fragment(string html, string? expected) =>
        Assert.Equal(expected, DropReader.ParseImageAddress(html));

    [Fact]
    public void Web_addresses_become_downloads()
    {
        var items = DropReader.FromAddress("https://a.example/cat.png");

        Assert.Equal(new Uri("https://a.example/cat.png"), items.Url);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/secrets.png")]
    [InlineData("ftp://a.example/x.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("\\\\server\\share\\x.png")]
    [InlineData("not an address")]
    [InlineData("")]
    public void Only_web_addresses_are_ever_fetched(string address)
    {
        var items = DropReader.FromAddress(address);

        Assert.Null(items.Url);
        Assert.Null(items.File);
    }

    [Fact]
    public void A_data_address_is_decoded_on_the_spot()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };

        var items = DropReader.FromAddress($"data:image/png;base64,{Convert.ToBase64String(bytes)}");

        Assert.Equal(bytes, items.File!.Data);
        Assert.Null(items.Url);
    }

    [Fact]
    public void A_damaged_data_address_gives_nothing() =>
        Assert.Null(DropReader.FromAddress("data:image/png;base64,@@@@").File);
}

public class ImageFetcherTests
{
    [Fact]
    public async Task Only_web_addresses_are_attempted() =>
        Assert.Null(await ImageFetcher.FetchAsync(new Uri("file:///C:/Windows/win.ini")));
}

public class BackgroundSettingsTests
{
    [Fact]
    public void The_ribbon_shows_by_default_and_a_change_persists()
    {
        var path = Path.Combine(TestImages.TempFolder(), "background.json");
        var store = new BackgroundSettingsStore(path);
        Assert.True(store.Load().ShowRibbon);

        store.Update(settings => settings.ShowRibbon = false);

        Assert.False(new BackgroundSettingsStore(path).Load().ShowRibbon);
    }

    [Fact]
    public void Each_process_keeps_what_the_other_saved()
    {
        var path = Path.Combine(TestImages.TempFolder(), "background.json");
        var app = new BackgroundSettingsStore(path);
        var helper = new BackgroundSettingsStore(path);

        app.Update(settings => settings.SaveFolder = "D:\\Shots");
        helper.Update(settings => settings.RibbonOnLeft = true);

        Assert.Equal("D:\\Shots", app.Load().SaveFolder);
        Assert.True(app.Load().RibbonOnLeft);
    }

    [Fact]
    public void The_default_save_folder_is_outside_any_redirected_pictures_folder()
    {
        var store = new BackgroundSettingsStore(Path.Combine(TestImages.TempFolder(), "background.json"));
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(Path.Combine(profile, "Pictures", "PicKit"), store.SaveFolder);
    }

    [Fact]
    public void Damaged_settings_fall_back_to_defaults()
    {
        var path = Path.Combine(TestImages.TempFolder(), "background.json");
        File.WriteAllText(path, "{ not json");

        Assert.True(new BackgroundSettingsStore(path).Load().ShowRibbon);
    }
}

public class BackgroundRegistrationTests
{
    private readonly string _sandboxPath = $@"Software\PicKitTests\{Guid.NewGuid():N}";

    [Fact]
    public void Starting_at_sign_in_can_be_turned_on_and_off()
    {
        try
        {
            using var sandbox = Registry.CurrentUser.CreateSubKey(_sandboxPath);
            using var classes = sandbox.CreateSubKey("Classes");
            using var software = sandbox.CreateSubKey("Software");
            var shell = new ShellRegistration(classes, software, @"C:\Apps\Pic Kit\PicKit.exe");
            Assert.False(shell.IsBackgroundRegistered);

            shell.RegisterBackground();
            Assert.True(shell.IsBackgroundRegistered);

            using (var run = software.OpenSubKey(@"Microsoft\Windows\CurrentVersion\Run"))
            {
                Assert.Equal("\"C:\\Apps\\Pic Kit\\PicKit.exe\" --background", run!.GetValue("PicKit"));
            }

            shell.UnregisterBackground();
            Assert.False(shell.IsBackgroundRegistered);

            shell.RegisterBackground();
            shell.RegisterNotifications();
            Assert.NotNull(classes.OpenSubKey(@"AppUserModelId\PicKit.Screenshots"));
            using (var command = classes.OpenSubKey(@"pickit\shell\open\command"))
            {
                Assert.Equal("\"C:\\Apps\\Pic Kit\\PicKit.exe\" --notification -- \"%1\"", command!.GetValue(null));
            }

            shell.UnregisterAll();
            Assert.False(shell.IsBackgroundRegistered);
            Assert.Null(classes.OpenSubKey("AppUserModelId"));
            Assert.Null(classes.OpenSubKey("pickit"));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(_sandboxPath, throwOnMissingSubKey: false);
            using (var parent = Registry.CurrentUser.OpenSubKey(@"Software\PicKitTests"))
            {
                if (parent is { SubKeyCount: 0 })
                {
                    Registry.CurrentUser.DeleteSubKey(@"Software\PicKitTests", throwOnMissingSubKey: false);
                }
            }
        }
    }

    [Theory]
    [InlineData("--background", ShellAction.Background)]
    [InlineData("--stop-background", ShellAction.StopBackground)]
    [InlineData("--screenshot", ShellAction.TakeScreenshot)]
    [InlineData("--register-background", ShellAction.RegisterBackground)]
    public void Background_switches_are_recognised(string argument, ShellAction expected) =>
        Assert.Equal(expected, LaunchRequest.Parse([argument]).Action);
}
