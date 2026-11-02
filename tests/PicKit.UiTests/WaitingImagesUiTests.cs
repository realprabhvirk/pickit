using System.IO;
using System.Windows;
using PicKit.Core.Background;
using PicKit.Core.Imaging;
using PicKit.Core.Settings;
using PicKit.Core.Shell;
using PicKit.Services;
using PicKit.ViewModels;
using SkiaSharp;

namespace PicKit.UiTests;

public class WaitingImagesUiTests
{
    private static byte[] Shot(SKColor color)
    {
        using var image = ImageBuffer.Create(400, 300);
        image.Bitmap.Erase(color);
        return ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png));
    }

    private static string Inbox(UiHarness harness) => Path.Combine(harness.Folder, ".inbox");

    private static string SaveFolder(UiHarness harness) => Path.Combine(harness.Folder, "Saved");

    private static string AddShot(UiHarness harness, string name, SKColor? color = null, int minutesAgo = 0)
    {
        var path = new ImageInbox(Inbox(harness)).Add(Shot(color ?? SKColors.Teal), name);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-minutesAgo));
        return path;
    }

    private static void OpenFromWindows(UiHarness harness)
    {
        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Open, []), bringToFront: false);
        harness.WaitFor(() => !harness.ViewModel.IsLoading);
        harness.Pump();
    }

    [Fact]
    public void Opening_from_windows_brings_up_a_waiting_screenshot_first() => UiHarness.Run(harness =>
    {
        var path = AddShot(harness, "Screenshot 2026-11-02 180312.png");

        OpenFromWindows(harness);

        var viewModel = harness.ViewModel;
        Assert.True(viewModel.HasImage);
        Assert.True(viewModel.IsWaitingImage);
        Assert.Equal(path, viewModel.WaitingPath);
        Assert.Contains("Screenshot 2026-11-02 180312.png", viewModel.WindowTitle);
        Assert.Null(viewModel.Editor.Document!.Source.Path);
        harness.Screenshot("waiting-screenshot");
    });

    [Fact]
    public void With_nothing_waiting_the_window_opens_empty() => UiHarness.Run(harness =>
    {
        OpenFromWindows(harness);

        Assert.False(harness.ViewModel.HasImage);
        Assert.False(harness.ViewModel.IsWaitingImage);
    });

    [Fact]
    public void The_newest_comes_first_and_the_rest_wait_in_the_strip() => UiHarness.Run(harness =>
    {
        AddShot(harness, "older.png", minutesAgo: 10);
        var newest = AddShot(harness, "newest.png");

        OpenFromWindows(harness);

        Assert.Equal(newest, harness.ViewModel.WaitingPath);
        Assert.Equal(2, harness.ViewModel.OpenImages.Count);
        Assert.True(harness.ViewModel.HasMultipleImages);
    });

    [Fact]
    public void Save_moves_it_to_the_save_folder_and_it_becomes_a_normal_file() => UiHarness.Run(async harness =>
    {
        var path = AddShot(harness, "shot.png");
        OpenFromWindows(harness);
        var viewModel = harness.ViewModel;

        await viewModel.KeepWaitingCommand.ExecuteAsync(null);
        harness.Pump();

        var saved = Path.Combine(SaveFolder(harness), "shot.png");
        Assert.True(File.Exists(saved));
        Assert.False(File.Exists(path));
        Assert.False(viewModel.IsWaitingImage);
        Assert.Equal(saved, viewModel.Editor.Document!.Source.Path);
        Assert.False(viewModel.Editor.Document.IsDirty);
        Assert.Contains(saved, viewModel.RecentImages.Select(image => image.Path));
    });

    [Fact]
    public void The_main_save_button_does_the_same_without_asking_where() => UiHarness.Run(async harness =>
    {
        AddShot(harness, "shot.png");
        OpenFromWindows(harness);

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.Empty(harness.Dialogs.Confirmations);
        Assert.True(File.Exists(Path.Combine(SaveFolder(harness), "shot.png")));
    });

    [Fact]
    public void Edits_made_before_saving_are_in_the_saved_file() => UiHarness.Run(async harness =>
    {
        AddShot(harness, "shot.png", SKColors.Teal);
        OpenFromWindows(harness);
        var editor = harness.ViewModel.Editor;

        await editor.ApplyAsync("Paint", source =>
        {
            var result = source.Clone();
            result.Bitmap.Erase(SKColors.Orange);
            return result;
        });
        harness.Pump();

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);
        harness.Pump();

        using var saved = ImageDecoder.Decode(Path.Combine(SaveFolder(harness), "shot.png"));
        Assert.Equal(SKColors.Orange, saved.Pixels.Bitmap.GetPixel(10, 10));
        Assert.False(editor.Document!.IsDirty);
        Assert.Empty(harness.Dialogs.Confirmations);
    });

    [Fact]
    public void Trash_removes_it_and_moves_on_to_the_next() => UiHarness.Run(async harness =>
    {
        var older = AddShot(harness, "older.png", minutesAgo: 10);
        var newest = AddShot(harness, "newest.png");
        OpenFromWindows(harness);
        var viewModel = harness.ViewModel;

        await viewModel.TrashWaitingCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.False(File.Exists(newest));
        Assert.Equal(older, viewModel.WaitingPath);
        Assert.Single(viewModel.OpenImages);
    });

    [Fact]
    public void Trashing_the_last_one_leaves_an_empty_window() => UiHarness.Run(async harness =>
    {
        var path = AddShot(harness, "shot.png");
        OpenFromWindows(harness);
        var viewModel = harness.ViewModel;

        await viewModel.TrashWaitingCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.False(File.Exists(path));
        Assert.False(viewModel.HasImage);
        Assert.False(viewModel.IsWaitingImage);
        Assert.Empty(viewModel.OpenImages);
        Assert.Equal("PicKit", viewModel.WindowTitle);
    });

    [Fact]
    public void Save_as_somewhere_else_uses_up_the_waiting_copy() => UiHarness.Run(async harness =>
    {
        var path = AddShot(harness, "shot.png");
        OpenFromWindows(harness);
        var target = Path.Combine(harness.Folder, "chosen.png");
        harness.Dialogs.SavePath = target;

        await harness.ViewModel.SaveAsCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.True(File.Exists(target));
        Assert.False(File.Exists(path));
        Assert.False(harness.ViewModel.IsWaitingImage);
        Assert.Equal(target, harness.ViewModel.Editor.Document!.Source.Path);
    });

    [Fact]
    public void Leaving_it_undecided_keeps_it_waiting_without_a_prompt() => UiHarness.Run(harness =>
    {
        var path = AddShot(harness, "shot.png");
        OpenFromWindows(harness);

        Assert.False(harness.ViewModel.HasUnsavedChanges);
        Assert.True(File.Exists(path));
        Assert.Equal(0, harness.Dialogs.UnsavedPrompts);
    });

    [Fact]
    public void A_waiting_image_is_not_added_to_recent_files() => UiHarness.Run(harness =>
    {
        var path = AddShot(harness, "shot.png");
        OpenFromWindows(harness);

        Assert.DoesNotContain(path, harness.ViewModel.RecentImages.Select(image => image.Path));
    });

    [Fact]
    public void A_screenshot_arriving_mid_edit_joins_the_strip_and_leaves_the_work_alone() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 800, 600);
        var viewModel = harness.ViewModel;
        var document = viewModel.Editor.Document;
        var path = AddShot(harness, "shot.png");

        viewModel.ShowWaitingImages();
        harness.Pump();

        Assert.Same(document, viewModel.Editor.Document);
        Assert.False(viewModel.IsWaitingImage);
        Assert.Contains(path, viewModel.OpenImages.Select(image => image.Path));
        Assert.True(viewModel.HasMultipleImages);
    });

    [Fact]
    public void Showing_waiting_images_twice_does_not_add_them_twice() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 800, 600);
        AddShot(harness, "shot.png");

        harness.ViewModel.ShowWaitingImages();
        harness.ViewModel.ShowWaitingImages();

        Assert.Equal(2, harness.ViewModel.OpenImages.Count);
    });

    [Fact]
    public void Picking_a_waiting_image_from_the_strip_offers_save_and_trash() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 800, 600);
        var viewModel = harness.ViewModel;
        var path = AddShot(harness, "shot.png");
        viewModel.ShowWaitingImages();

        viewModel.SelectedImage = viewModel.OpenImages.First(image => image.Path == path);
        harness.WaitFor(() => viewModel.IsWaitingImage);

        Assert.Equal(path, viewModel.WaitingPath);
    });

    [Fact]
    public void An_image_dragged_from_a_web_page_opens_ready_to_edit() => UiHarness.Run(async harness =>
    {
        await harness.ViewModel.OpenDroppedAsync(new DroppedItems([], new DroppedFile("cat.jpg", Shot(SKColors.Pink)), null));
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);

        Assert.True(harness.ViewModel.IsWaitingImage);
        Assert.Contains("cat.png", harness.ViewModel.WindowTitle);
    });

    [Fact]
    public void Dropping_something_that_is_not_an_image_says_so() => UiHarness.Run(async harness =>
    {
        await harness.ViewModel.OpenDroppedAsync(new DroppedItems([], new DroppedFile("page.jpg", new byte[64]), null));
        Assert.Equal("That isn't an image PicKit can open", harness.ViewModel.NoticeText);

        await harness.ViewModel.OpenDroppedAsync(DroppedItems.Nothing);
        Assert.False(harness.ViewModel.HasImage);
    });

    [Fact]
    public void Dropped_files_open_as_normal_files() => UiHarness.Run(async harness =>
    {
        var path = Path.Combine(harness.Folder, "real.png");
        File.WriteAllBytes(path, Shot(SKColors.Teal));

        await harness.ViewModel.OpenDroppedAsync(new DroppedItems([path], null, null));
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);

        Assert.False(harness.ViewModel.IsWaitingImage);
        Assert.Equal(path, harness.ViewModel.Editor.Document!.Source.Path);
    });
}

public class BackgroundSettingsUiTests
{
    private sealed class CountingControl : BackgroundControl
    {
        public int Starts { get; private set; }

        public int Stops { get; private set; }

        public int Restarts { get; private set; }

        public override void Start() => Starts++;

        public override void Stop() => Stops++;

        public override void Restart() => Restarts++;
    }

    private static void WithSettings(UiHarness harness, Action<SettingsViewModel, CountingControl, ShellRegistration, BackgroundSettingsStore> test)
    {
        var path = $@"Software\PicKitTests\{Guid.NewGuid():N}";
        try
        {
            using var sandbox = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(path);
            using var classes = sandbox.CreateSubKey("Classes");
            using var software = sandbox.CreateSubKey("Software");
            var shell = new ShellRegistration(classes, software, @"C:\PicKit\PicKit.exe");
            var store = new BackgroundSettingsStore(Path.Combine(harness.Folder, "settings-background.json"));
            var control = new CountingControl();
            var settings = new SettingsViewModel(
                new SettingsStore(Path.Combine(harness.Folder, "settings-test.json")),
                new RecentFiles(Path.Combine(harness.Folder, "settings-recent.json")),
                shell,
                () => { },
                store,
                control,
                harness.Dialogs);

            test(settings, control, shell, store);
        }
        finally
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            using (var parent = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\PicKitTests"))
            {
                if (parent is { SubKeyCount: 0 })
                {
                    Microsoft.Win32.Registry.CurrentUser.DeleteSubKey(@"Software\PicKitTests", throwOnMissingSubKey: false);
                }
            }
        }
    }

    [Fact]
    public void Turning_background_running_on_and_off_starts_and_stops_the_helper() => UiHarness.Run(harness =>
        WithSettings(harness, (settings, control, shell, _) =>
        {
            settings.RunInBackground = true;
            Assert.True(shell.IsBackgroundRegistered);
            Assert.Equal(1, control.Starts);

            settings.RunInBackground = false;
            Assert.False(shell.IsBackgroundRegistered);
            Assert.Equal(1, control.Stops);
        }));

    [Fact]
    public void Hiding_the_ribbon_is_saved_and_applied_to_a_running_helper() => UiHarness.Run(harness =>
        WithSettings(harness, (settings, control, _, store) =>
        {
            settings.RunInBackground = true;

            settings.ShowRibbon = false;

            Assert.False(store.Load().ShowRibbon);
            Assert.Equal(1, control.Restarts);
        }));

    [Fact]
    public void A_new_save_folder_is_kept() => UiHarness.Run(harness =>
        WithSettings(harness, (settings, _, _, store) =>
        {
            harness.Dialogs.FolderPath = @"D:\Shots";

            settings.ChooseSaveFolderCommand.Execute(null);

            Assert.Equal(@"D:\Shots", settings.SaveFolder);
            Assert.Equal(@"D:\Shots", store.SaveFolder);
        }));
}
