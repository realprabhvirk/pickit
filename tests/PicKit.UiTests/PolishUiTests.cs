using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PicKit.Core.Imaging;
using PicKit.Core.Settings;
using PicKit.Core.Shell;
using PicKit.Services;
using PicKit.ViewModels;
using PicKit.Views;
using SkiaSharp;

namespace PicKit.UiTests;

public class PolishUiTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void Pointer_maps_to_the_right_image_pixel_at_every_display_scale(double scale) => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1600, 1000);
        var canvas = harness.Window.Canvas;
        VisualTreeHelper.SetRootDpi(harness.Window, new DpiScale(scale, scale));
        canvas.InvalidateVisual();
        harness.Pump();

        // A drag between two known image points must land on those exact points.
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Redact;
        harness.Pump();

        var from = TestFiles.ToElement(canvas, new SKPoint(400, 300));
        var to = TestFiles.ToElement(canvas, new SKPoint(800, 600));
        canvas.PressAt(from, MouseButton.Left, ModifierKeys.None);
        canvas.MoveTo(to, ModifierKeys.None);
        canvas.ReleaseAt(to, MouseButton.Left, ModifierKeys.None);
        harness.Pump();

        var box = Assert.Single(editor.Redact.Pending);
        Assert.Equal(400, box.Left, 3f);
        Assert.Equal(300, box.Top, 3f);
        Assert.Equal(800, box.Right, 3f);
        Assert.Equal(600, box.Bottom, 3f);

        harness.Screenshot($"dpi-{(int)(scale * 100)}");
        VisualTreeHelper.SetRootDpi(harness.Window, new DpiScale(1.5, 1.5));
    });

    [Fact]
    public void Light_theme_renders_cleanly() => UiHarness.Run(async harness =>
    {
        AppearanceService.Apply(AppAppearance.Light);
        try
        {
            await TestFiles.OpenPhoto(harness, 1200, 800);
            var editor = harness.ViewModel.Editor;
            editor.ActiveTool = editor.Crop;
            harness.Pump();
            harness.Screenshot("light-crop");

            editor.ActiveTool = editor.Resize;
            harness.Pump();
            harness.Screenshot("light-resize");
        }
        finally
        {
            AppearanceService.Apply(AppAppearance.System);
            harness.Pump();
        }
    });

    [Fact]
    public void Recent_files_appear_in_the_empty_window_and_clear() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 200, 100);
        var path = harness.ViewModel.Editor.Document!.Source.Path!;

        Assert.Contains(path, harness.ViewModel.RecentImages.Select(image => image.Path));

        harness.ViewModel.ClearRecent();
        Assert.Empty(harness.ViewModel.RecentImages);
    });

    [Fact]
    public void Empty_window_shows_recents() => UiHarness.Run(async harness =>
    {
        var path = Path.Combine(harness.Folder, "earlier.png");
        using (var image = ImageBuffer.Create(40, 40))
        {
            await File.WriteAllBytesAsync(path, ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png)));
        }

        harness.ViewModel.RememberRecent(path);
        harness.Pump();
        harness.Screenshot("empty-with-recent");

        Assert.Single(harness.ViewModel.RecentImages);

        harness.ViewModel.OpenRecentCommand.Execute(harness.ViewModel.RecentImages[0]);
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);
        Assert.Equal(path, harness.ViewModel.Editor.Document!.Source.Path);
    });

    [Fact]
    public void Settings_save_as_they_change_and_reflect_integration_state() => UiHarness.Run(harness =>
    {
        var sandbox = $@"Software\PicKitTests\{Guid.NewGuid():N}";
        using var classes = Registry.CurrentUser.CreateSubKey($@"{sandbox}\Classes");
        using var software = Registry.CurrentUser.CreateSubKey($@"{sandbox}\Software");
        try
        {
            var path = Path.Combine(harness.Folder, "settings.json");
            var store = new SettingsStore(path);
            var shell = new ShellRegistration(classes, software, @"C:\Apps\PicKit.exe");
            var cleared = false;
            var settings = new SettingsViewModel(store, new RecentFiles(Path.Combine(harness.Folder, "recent.json")), shell, () => cleared = true);

            Assert.False(settings.ExplorerMenu);

            settings.JpegQuality = 77;
            settings.ConfirmOverwrite = false;
            settings.KeepPhotoDetails = true;
            settings.ExplorerMenu = true;
            settings.ClearRecentCommand.Execute(null);

            var reloaded = new SettingsStore(path).Current;
            Assert.Equal(77, reloaded.JpegQuality);
            Assert.False(reloaded.ConfirmOverwrite);
            Assert.Equal(MetadataHandling.Keep, reloaded.MetadataHandling);
            Assert.True(shell.IsMenuRegistered);
            Assert.True(cleared);

            settings.ExplorerMenu = false;
            Assert.False(shell.IsMenuRegistered);

            var window = new SettingsWindow { DataContext = settings, ShowActivated = false, Left = -6000, Top = -6000, WindowStartupLocation = WindowStartupLocation.Manual };
            window.Show();
            harness.Pump();
            window.Close();
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(sandbox, throwOnMissingSubKey: false);
            using (var parent = Registry.CurrentUser.OpenSubKey(@"Software\PicKitTests"))
            {
                if (parent is { SubKeyCount: 0 })
                {
                    Registry.CurrentUser.DeleteSubKey(@"Software\PicKitTests", throwOnMissingSubKey: false);
                }
            }
        }
    });

    [Fact]
    public void Saving_respects_the_replace_prompt_setting() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 200, 100);
        harness.Settings.Current.ConfirmOverwrite = false;
        await harness.ViewModel.Editor.Rotate.RotateRightCommand.ExecuteAsync(null);
        harness.Pump();

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.Empty(harness.Dialogs.Confirmations);
        Assert.False(harness.ViewModel.Editor.Document!.IsDirty);
    });

    [Fact]
    public void Every_tool_and_command_has_an_accessible_name() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 200, 100);
        harness.Pump();

        var unnamed = new List<string>();
        foreach (var control in Descendants(harness.Window).OfType<ButtonBase>().Where(button => button.IsVisible))
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(control);
            if (string.IsNullOrWhiteSpace(peer?.GetName()))
            {
                unnamed.Add(control.GetType().Name + " " + control.Content);
            }
        }

        Assert.Empty(unnamed);

        var rail = Descendants(harness.Window).OfType<ListBox>().First(list => AutomationProperties.GetName(list) == "Tools");
        var item = (ListBoxItem)rail.ItemContainerGenerator.ContainerFromIndex(0);
        Assert.Equal("Select", UIElementAutomationPeer.CreatePeerForElement(item)!.GetName());
    });

    [Fact]
    public void Tools_panel_buttons_are_reachable_by_keyboard() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 400, 300);
        var editor = harness.ViewModel.Editor;

        editor.ActiveTool = editor.Crop;
        harness.Pump();

        var buttons = Descendants(harness.Window).OfType<Button>().Where(button => button.IsVisible).ToList();
        Assert.All(buttons, button =>
        {
            Assert.True(button.Focusable);
            Assert.True(KeyboardNavigation.GetIsTabStop(button));
        });
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }
}
