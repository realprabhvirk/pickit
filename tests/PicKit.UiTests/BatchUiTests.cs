using System.IO;
using PicKit.Core.Batch;
using PicKit.Core.Imaging;
using PicKit.Core.Shell;
using PicKit.ViewModels;

namespace PicKit.UiTests;

public class BatchUiTests
{
    private static async Task<List<string>> WritePhotos(UiHarness harness, int count, int width = 300, int height = 200)
    {
        var paths = new List<string>();
        for (var i = 1; i <= count; i++)
        {
            var path = Path.Combine(harness.Folder, $"photo{i}.png");
            using var image = ImageBuffer.Create(width, height);
            image.Bitmap.Erase(SkiaSharp.SKColors.SteelBlue);
            await File.WriteAllBytesAsync(path, ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png)));
            paths.Add(path);
        }

        return paths;
    }

    private static BatchViewModel Batch(UiHarness harness) => Assert.IsType<BatchViewModel>(harness.ViewModel.Page);

    private static RenameViewModel Rename(UiHarness harness) => Assert.IsType<RenameViewModel>(harness.ViewModel.Page);

    [Fact]
    public void Selecting_many_images_for_compress_opens_the_batch_page() => UiHarness.Run(async harness =>
    {
        var paths = await WritePhotos(harness, 5);

        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Compress, paths));
        harness.WaitFor(() => harness.ViewModel.Page is not null);
        harness.Pump();
        harness.Screenshot("batch-compress");

        var batch = Batch(harness);
        Assert.Equal("Compress 5 images", batch.Heading);
        Assert.Equal(BatchOperation.Compress, batch.Operation);
    });

    [Fact]
    public void Batch_runs_shows_progress_and_reports_failures() => UiHarness.Run(async harness =>
    {
        var paths = await WritePhotos(harness, 4);
        var broken = Path.Combine(harness.Folder, "broken.png");
        await File.WriteAllBytesAsync(broken, [1, 2, 3]);
        paths.Add(broken);

        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Convert, paths));
        harness.WaitFor(() => harness.ViewModel.Page is not null);
        var batch = Batch(harness);
        Assert.Equal("Convert 5 images", batch.Heading);
        harness.Screenshot("batch-convert");

        await batch.StartCommand.ExecuteAsync(null);
        harness.Pump();
        harness.Screenshot("batch-finished");

        Assert.True(batch.IsFinished);
        Assert.Equal(5, batch.Completed);
        Assert.Contains("4 images saved", batch.ResultText);
        Assert.Contains("1 couldn't", batch.ResultText);
        Assert.Equal("broken.png", Assert.Single(batch.Failures).Name);
        Assert.True(File.Exists(Path.Combine(harness.Folder, "photo1-webp.webp")));
    });

    [Fact]
    public void Batch_resize_uses_the_entered_size() => UiHarness.Run(async harness =>
    {
        var paths = await WritePhotos(harness, 2);

        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Resize, paths));
        harness.WaitFor(() => harness.ViewModel.Page is not null);
        var batch = Batch(harness);
        batch.WidthText = "150";
        batch.HeightText = "150";
        harness.Screenshot("batch-resize");

        await batch.StartCommand.ExecuteAsync(null);
        harness.Pump();

        using var result = ImageDecoder.Decode(Path.Combine(harness.Folder, "photo1-resized.png"));
        Assert.Equal((150, 100), (result.Pixels.Width, result.Pixels.Height));
    });

    [Fact]
    public void Crop_mode_warns_before_anything_is_cut() => UiHarness.Run(async harness =>
    {
        var paths = await WritePhotos(harness, 2);
        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Resize, paths));
        harness.WaitFor(() => harness.ViewModel.Page is not null);
        var batch = Batch(harness);

        Assert.Null(batch.CropNote);
        batch.ResizeMode = PicKit.Core.Imaging.ResizeMode.CropToFill;

        Assert.Contains("removed", batch.CropNote);
    });

    [Fact]
    public void Invalid_sizes_and_a_missing_folder_block_start() => UiHarness.Run(async harness =>
    {
        var paths = await WritePhotos(harness, 2);
        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Resize, paths));
        harness.WaitFor(() => harness.ViewModel.Page is not null);
        var batch = Batch(harness);

        batch.WidthText = "wide";
        Assert.False(batch.StartCommand.CanExecute(null));

        batch.WidthText = "100";
        Assert.True(batch.StartCommand.CanExecute(null));

        batch.Output = BatchOutput.ChosenFolder;
        Assert.False(batch.StartCommand.CanExecute(null));

        harness.Dialogs.FolderPath = harness.Folder;
        batch.ChooseFolderCommand.Execute(null);
        Assert.True(batch.StartCommand.CanExecute(null));
    });

    [Fact]
    public void Closing_the_page_returns_to_the_editor() => UiHarness.Run(async harness =>
    {
        var paths = await WritePhotos(harness, 2);
        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Compress, paths));
        harness.WaitFor(() => harness.ViewModel.Page is not null);

        Batch(harness).CloseCommand.Execute(null);
        harness.Pump();

        Assert.Null(harness.ViewModel.Page);
    });

    [Fact]
    public void A_single_image_opens_in_the_tool_rather_than_the_batch_page() => UiHarness.Run(async harness =>
    {
        var paths = await WritePhotos(harness, 1);

        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Compress, paths));
        harness.WaitFor(() => harness.ViewModel.Editor.ActiveTool is not null);

        Assert.Null(harness.ViewModel.Page);
        Assert.Same(harness.ViewModel.Editor.Compress, harness.ViewModel.Editor.ActiveTool);
    });

    [Fact]
    public void Opening_several_images_gives_each_a_tab_to_move_between() => UiHarness.Run(async harness =>
    {
        var paths = await WritePhotos(harness, 3);

        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Open, paths));
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);
        harness.Pump();
        harness.Screenshot("image-strip");

        Assert.True(harness.ViewModel.HasMultipleImages);
        Assert.Equal(3, harness.ViewModel.Tabs.Count);
        Assert.Equal(paths[0], harness.ViewModel.Editor.Document!.Source.Path);

        harness.ViewModel.SelectedTab = harness.ViewModel.Tabs[2];
        harness.WaitFor(() => harness.ViewModel.Editor.Document!.Source.Path == paths[2] && !harness.ViewModel.IsLoading);
        Assert.Equal(paths[2], harness.ViewModel.Editor.Document!.Source.Path);
    });

    [Fact]
    public void Switching_images_keeps_unsaved_edits_without_asking() => UiHarness.Run(async harness =>
    {
        var paths = await WritePhotos(harness, 2);
        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Open, paths));
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);
        var editor = harness.ViewModel.Editor;
        await editor.Rotate.RotateRightCommand.ExecuteAsync(null);
        harness.Pump();

        harness.ViewModel.SelectedTab = harness.ViewModel.Tabs[1];
        harness.WaitFor(() => editor.Document!.Source.Path == paths[1] && !harness.ViewModel.IsLoading);

        Assert.Equal(0, harness.Dialogs.UnsavedPrompts);
        Assert.True(harness.ViewModel.Tabs[0].IsDirty);
    });

    [Fact]
    public void Open_images_can_be_sent_to_a_batch() => UiHarness.Run(async harness =>
    {
        var paths = await WritePhotos(harness, 3);
        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Open, paths));
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);

        harness.ViewModel.BatchAllCommand.Execute(BatchOperation.Resize);

        Assert.Equal("Resize 3 images", Batch(harness).Heading);
    });

    [Fact]
    public void A_dropped_folder_opens_its_images() => UiHarness.Run(async harness =>
    {
        await WritePhotos(harness, 2);

        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Open, [harness.Folder]));
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);

        Assert.Equal(2, harness.ViewModel.Tabs.Count);
    });

    [Fact]
    public void Rename_previews_applies_and_undoes() => UiHarness.Run(async harness =>
    {
        var paths = await WritePhotos(harness, 3);

        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Rename, paths));
        harness.WaitFor(() => harness.ViewModel.Page is not null);
        var rename = Rename(harness);
        rename.Pattern = "Holiday_{number}";
        harness.Pump();
        harness.Screenshot("rename-preview");

        Assert.Equal("Rename 3 images", rename.Heading);
        Assert.Equal("3 files will be renamed.", rename.ConfirmText);
        Assert.Equal("Holiday_001.png", rename.Preview[0].NewName);
        Assert.True(File.Exists(paths[0]));

        rename.RenameCommand.Execute(null);
        harness.Pump();

        Assert.True(File.Exists(Path.Combine(harness.Folder, "Holiday_003.png")));
        Assert.False(File.Exists(paths[0]));
        Assert.Contains("3 files renamed", rename.ResultText);
        Assert.True(rename.CanUndoRename);

        rename.UndoCommand.Execute(null);
        harness.Pump();

        Assert.True(File.Exists(paths[0]));
        Assert.False(File.Exists(Path.Combine(harness.Folder, "Holiday_001.png")));
    });

    [Fact]
    public void Rename_stops_and_explains_a_collision() => UiHarness.Run(async harness =>
    {
        var paths = await WritePhotos(harness, 2);
        var taken = Path.Combine(harness.Folder, "Holiday_001.png");
        await File.WriteAllBytesAsync(taken, [7]);

        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Rename, paths));
        harness.WaitFor(() => harness.ViewModel.Page is not null);
        var rename = Rename(harness);
        rename.Pattern = "Holiday_{number}";
        harness.Pump();
        harness.Screenshot("rename-collision");

        Assert.Contains("already exists", rename.ProblemText);
        Assert.False(rename.RenameCommand.CanExecute(null));
        Assert.Equal(new byte[] { 7 }, await File.ReadAllBytesAsync(taken));
    });

    [Fact]
    public void Renaming_the_open_image_keeps_the_editor_pointing_at_it() => UiHarness.Run(async harness =>
    {
        var paths = await WritePhotos(harness, 2);
        harness.ViewModel.Handle(new LaunchRequest(ShellAction.Open, paths));
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);

        harness.ViewModel.RenameAllCommand.Execute(null);
        var rename = Rename(harness);
        rename.Pattern = "Trip_{number}";
        harness.Pump();
        rename.RenameCommand.Execute(null);
        harness.Pump();

        var expected = Path.Combine(harness.Folder, "Trip_001.png");
        Assert.Equal(expected, harness.ViewModel.Editor.Document!.Source.Path);
        Assert.Equal(expected, harness.ViewModel.Tabs[0].Path);
    });
}
