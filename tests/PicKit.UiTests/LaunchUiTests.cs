using System.IO;
using PicKit.Core.Imaging;
using PicKit.Core.Shell;
using PicKit.ViewModels;

namespace PicKit.UiTests;

public class LaunchUiTests
{
    private static async Task<string> WritePhoto(UiHarness harness, string name, int width = 300, int height = 200)
    {
        var path = Path.Combine(harness.Folder, name);
        using var image = ImageBuffer.Create(width, height);
        image.Bitmap.Erase(SkiaSharp.SKColors.SteelBlue);
        await File.WriteAllBytesAsync(path, ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png)));
        return path;
    }

    [Theory]
    [InlineData(ShellAction.Compress)]
    [InlineData(ShellAction.Convert)]
    public void Tool_actions_open_the_image_on_the_tools_tab(ShellAction action) => UiHarness.Run(async harness =>
    {
        var path = await WritePhoto(harness, "photo.png");

        harness.ViewModel.Handle(new LaunchRequest(action, [path]));
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading && harness.ViewModel.Editor.ActiveTool is not null);

        var editor = harness.ViewModel.Editor;
        Assert.Equal(EditorTab.Tools, editor.SelectedTab);
        Assert.Equal(action == ShellAction.Compress ? "Compress" : "Convert", editor.ActiveTool!.Name);
    });

    [Fact]
    public void Resize_action_opens_the_resize_tool() => UiHarness.Run(async harness =>
    {
        var path = await WritePhoto(harness, "photo.png");

        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Resize, [path]));
        harness.WaitFor(() => harness.ViewModel.Editor.ActiveTool is not null);

        Assert.Same(harness.ViewModel.Editor.Resize, harness.ViewModel.Editor.ActiveTool);
    });

    [Fact]
    public void Open_action_with_nothing_just_shows_the_window() => UiHarness.Run(harness =>
    {
        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Open, []));
        harness.Pump();

        Assert.False(harness.ViewModel.HasImage);
        Assert.Empty(harness.Dialogs.Errors);
    });

    [Fact]
    public void Copy_action_puts_the_file_on_the_clipboard() => UiHarness.Run(async harness =>
    {
        var path = await WritePhoto(harness, "photo.png", 120, 90);

        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Copy, [path]));
        harness.WaitFor(() => harness.Clipboard.Contents is not null);

        using var pasted = harness.Clipboard.ReadImage()!;
        Assert.Equal(120, pasted.Width);
    });

    [Fact]
    public void Non_images_in_a_selection_are_skipped() => UiHarness.Run(async harness =>
    {
        var path = await WritePhoto(harness, "photo.png");
        var notes = Path.Combine(harness.Folder, "notes.txt");
        await File.WriteAllTextAsync(notes, "hello");

        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Open, [notes, path]));
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);

        Assert.Equal(path, harness.ViewModel.Editor.Document!.Source.Path);
    });
}

public class LaunchInboxTests
{
    [Fact]
    public void One_process_per_file_merges_into_a_single_job()
    {
        var inbox = new LaunchInbox();
        for (var i = 0; i < 50; i++)
        {
            inbox.Add(new LaunchRequest(ShellAction.Compress, [$"{i}.jpg"]));
        }

        var job = Assert.Single(inbox.TakeMerged());

        Assert.Equal(ShellAction.Compress, job.Action);
        Assert.Equal(50, job.Paths.Count);
        Assert.Equal("0.jpg", job.Paths[0]);
        Assert.Equal("49.jpg", job.Paths[49]);
    }

    [Fact]
    public void Different_actions_stay_separate()
    {
        var inbox = new LaunchInbox();
        inbox.Add(new LaunchRequest(ShellAction.Compress, ["a.jpg"]));
        inbox.Add(new LaunchRequest(ShellAction.Resize, ["b.jpg"]));

        Assert.Equal(2, inbox.TakeMerged().Count);
        Assert.Empty(inbox.TakeMerged());
    }
}
