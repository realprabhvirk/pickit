using System.IO;
using System.Windows.Input;
using PicKit.Core.Imaging;
using PicKit.Core.Metadata;
using PicKit.Services;
using PicKit.ViewModels;
using PicKit.ViewModels.Tools;
using SkiaSharp;

namespace PicKit.UiTests;

public class SaveUiTests
{
    private static async Task Rotate(UiHarness harness)
    {
        await harness.ViewModel.Editor.Rotate.RotateRightCommand.ExecuteAsync(null);
        harness.Pump();
    }

    private static string PhotoPath(UiHarness harness) => Path.Combine(harness.Folder, "photo.png");

    [Fact]
    public void Save_asks_before_replacing_and_writes_the_edit() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 400, 200);
        await Rotate(harness);

        await harness.ViewModel.SaveCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.Contains("replace the existing file", Assert.Single(harness.Dialogs.Confirmations));
        using var saved = ImageDecoder.Decode(PhotoPath(harness));
        Assert.Equal((200, 400), (saved.Pixels.Width, saved.Pixels.Height));
        Assert.False(harness.ViewModel.Editor.Document!.IsDirty);
    });

    [Fact]
    public void Declining_the_replace_prompt_leaves_the_file_alone() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 400, 200);
        var before = await File.ReadAllBytesAsync(PhotoPath(harness));
        await Rotate(harness);

        harness.Dialogs.ConfirmAnswer = false;
        await harness.ViewModel.SaveCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.Equal(before, await File.ReadAllBytesAsync(PhotoPath(harness)));
        Assert.True(harness.ViewModel.Editor.Document!.IsDirty);
    });

    [Fact]
    public void Save_copy_creates_a_named_file_and_keeps_the_original() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 400, 200);
        var before = await File.ReadAllBytesAsync(PhotoPath(harness));
        await Rotate(harness);

        await harness.ViewModel.SaveCopyCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.Equal(before, await File.ReadAllBytesAsync(PhotoPath(harness)));
        using var copy = ImageDecoder.Decode(Path.Combine(harness.Folder, "photo-edited.png"));
        Assert.Equal(200, copy.Pixels.Width);
        Assert.Empty(harness.Dialogs.Confirmations);
    });

    [Fact]
    public void Save_as_changes_format_from_the_extension_and_adopts_the_new_file() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 400, 200);
        var target = Path.Combine(harness.Folder, "converted.webp");
        harness.Dialogs.SavePath = target;

        await harness.ViewModel.SaveAsCommand.ExecuteAsync(null);
        harness.Pump();

        using var saved = ImageDecoder.Decode(target);
        Assert.Equal(ImageFormat.WebP, saved.Format);
        Assert.Equal(target, harness.ViewModel.Editor.Document!.Source.Path);
        Assert.Contains("converted.webp", harness.ViewModel.WindowTitle);
    });

    [Fact]
    public void Save_includes_markup_and_pending_redactions() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 400, 200);
        var editor = harness.ViewModel.Editor;
        var canvas = harness.Window.Canvas;

        editor.ActiveTool = editor.Redact;
        harness.Pump();
        var from = TestFiles.ToElement(canvas, new SKPoint(100, 50));
        var to = TestFiles.ToElement(canvas, new SKPoint(300, 150));
        canvas.PressAt(from, MouseButton.Left, ModifierKeys.None);
        canvas.MoveTo(to, ModifierKeys.None);
        canvas.ReleaseAt(to, MouseButton.Left, ModifierKeys.None);
        harness.Pump();

        // The user marked an area but never pressed Apply. The file must still match the screen.
        var target = Path.Combine(harness.Folder, "out.png");
        harness.Dialogs.SavePath = target;
        await harness.ViewModel.SaveAsCommand.ExecuteAsync(null);
        harness.Pump();

        using var saved = ImageDecoder.Decode(target);
        Assert.Equal(0xFF000000, saved.Pixels.Pixels[(100 * 400) + 200]);
    });

    [Fact]
    public void Saving_transparency_as_jpeg_asks_what_to_put_behind_it() => UiHarness.Run(async harness =>
    {
        var source = Path.Combine(harness.Folder, "logo.png");
        using (var image = ImageBuffer.Create(64, 64))
        {
            await File.WriteAllBytesAsync(source, ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png)));
        }

        harness.ViewModel.OpenPaths([source]);
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);

        var target = Path.Combine(harness.Folder, "logo.jpg");
        harness.Dialogs.SavePath = target;
        harness.Dialogs.BackgroundAnswer = SKColors.White;
        await harness.ViewModel.SaveAsCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.Equal(1, harness.Dialogs.BackgroundPrompts);
        using var saved = ImageDecoder.Decode(target);
        Assert.True((saved.Pixels.Pixels[0] & 0xFF) > 250);
    });

    [Fact]
    public void Cancelling_the_background_question_writes_nothing() => UiHarness.Run(async harness =>
    {
        var source = Path.Combine(harness.Folder, "logo.png");
        using (var image = ImageBuffer.Create(64, 64))
        {
            await File.WriteAllBytesAsync(source, ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png)));
        }

        harness.ViewModel.OpenPaths([source]);
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);

        var target = Path.Combine(harness.Folder, "logo.jpg");
        harness.Dialogs.SavePath = target;
        harness.Dialogs.BackgroundAnswer = null;
        await harness.ViewModel.SaveAsCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.False(File.Exists(target));
    });

    [Fact]
    public void Reopening_the_same_image_goes_back_to_its_tab_with_edits_intact() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 400, 200);
        await Rotate(harness);
        var document = harness.ViewModel.Editor.Document;

        harness.ViewModel.OpenPaths([PhotoPath(harness)]);
        harness.WaitFor(() => !harness.ViewModel.IsLoading);
        harness.Pump();

        Assert.Equal(0, harness.Dialogs.UnsavedPrompts);
        Assert.Same(document, harness.ViewModel.Editor.Document);
        Assert.Single(harness.ViewModel.Tabs);
        Assert.True(document!.IsDirty);
    });

    [Fact]
    public void Closing_with_a_cancel_keeps_the_window_and_the_work() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 400, 200);
        await Rotate(harness);

        harness.Dialogs.UnsavedAnswer = UnsavedChoice.Cancel;
        harness.Window.Close();
        harness.Pump();

        Assert.Equal(1, harness.Dialogs.UnsavedPrompts);
        Assert.True(harness.Window.IsLoaded);
        Assert.True(harness.ViewModel.Editor.Document!.IsDirty);
    });

    [Fact]
    public void Clean_documents_never_prompt() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 400, 200);

        harness.ViewModel.OpenPaths([PhotoPath(harness)]);
        harness.WaitFor(() => !harness.ViewModel.IsLoading);
        harness.Pump();

        Assert.Equal(0, harness.Dialogs.UnsavedPrompts);
    });

    [Fact]
    public void Compress_writes_a_smaller_copy_and_reports_the_real_size() => UiHarness.Run(async harness =>
    {
        var source = Path.Combine(harness.Folder, "photo.png");
        await File.WriteAllBytesAsync(source, Noise(1000, 700, ImageFormat.Png));
        harness.ViewModel.OpenPaths([source]);
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);

        var editor = harness.ViewModel.Editor;
        editor.SelectedTab = EditorTab.Tools;
        editor.ActiveTool = editor.Compress;
        editor.Compress.SelectedFormat = editor.Compress.Formats.First(choice => choice.Label == "JPEG");
        harness.Pump();
        harness.WaitFor(() => editor.Compress.EstimateText.StartsWith("About"));
        harness.Screenshot("compress-panel");

        await editor.Compress.CompressCommand.ExecuteAsync(null);
        harness.Pump();

        var output = Path.Combine(harness.Folder, "photo-compressed.jpg");
        Assert.True(File.Exists(output), editor.Compress.ProblemText);
        Assert.True(new FileInfo(output).Length < new FileInfo(source).Length);
        Assert.Contains("photo-compressed.jpg", editor.Compress.ResultText);
        Assert.True(editor.Compress.CanShowResult);
    });

    [Fact]
    public void Compress_to_target_lands_under_it() => UiHarness.Run(async harness =>
    {
        var source = Path.Combine(harness.Folder, "photo.jpg");
        await File.WriteAllBytesAsync(source, Noise(1600, 1200, ImageFormat.Jpeg));
        harness.ViewModel.OpenPaths([source]);
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);

        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Compress;
        editor.Compress.Mode = CompressMode.TargetSize;
        editor.Compress.TargetText = "0.1";
        harness.Pump();

        await editor.Compress.CompressCommand.ExecuteAsync(null);
        harness.Pump();
        harness.Screenshot("compress-target-result");

        var output = Path.Combine(harness.Folder, "photo-compressed.jpg");
        Assert.True(new FileInfo(output).Length <= 0.1 * 1024 * 1024);
    });

    [Fact]
    public void Compress_rejects_a_nonsense_target() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 400, 200);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Compress;
        editor.Compress.Mode = CompressMode.TargetSize;

        editor.Compress.TargetText = "lots";

        Assert.False(editor.Compress.CompressCommand.CanExecute(null));
    });

    [Fact]
    public void Convert_names_the_output_and_asks_only_when_it_must() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 400, 200);
        var editor = harness.ViewModel.Editor;
        editor.SelectedTab = EditorTab.Tools;
        editor.ActiveTool = editor.Convert;
        editor.Convert.SelectedFormat = editor.Convert.Formats.First(choice => choice.Label == "WebP");
        harness.Pump();

        Assert.False(editor.Convert.NeedsFill);
        harness.Screenshot("convert-panel");

        await editor.Convert.ConvertCommand.ExecuteAsync(null);
        harness.Pump();

        using var converted = ImageDecoder.Decode(Path.Combine(harness.Folder, "photo-webp.webp"));
        Assert.Equal(ImageFormat.WebP, converted.Format);
    });

    [Fact]
    public void Convert_to_jpeg_offers_a_fill_for_transparency() => UiHarness.Run(async harness =>
    {
        var source = Path.Combine(harness.Folder, "logo.png");
        using (var image = ImageBuffer.Create(64, 64))
        {
            await File.WriteAllBytesAsync(source, ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png)));
        }

        harness.ViewModel.OpenPaths([source]);
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);

        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Convert;
        editor.Convert.SelectedFormat = editor.Convert.Formats.First(choice => choice.Label == "JPEG");
        editor.Convert.Fill = TransparencyFill.Black;
        harness.Pump();
        Assert.True(editor.Convert.NeedsFill);
        harness.Screenshot("convert-fill");

        await editor.Convert.ConvertCommand.ExecuteAsync(null);
        harness.Pump();

        using var converted = ImageDecoder.Decode(Path.Combine(harness.Folder, "logo-jpeg.jpg"));
        Assert.True((converted.Pixels.Pixels[0] & 0xFF) < 5);
    });

    [Fact]
    public void Details_shows_location_and_removes_it_from_a_copy() => UiHarness.Run(async harness =>
    {
        var source = Path.Combine(harness.Folder, "trip.jpg");
        await File.WriteAllBytesAsync(source, ExifJpeg.Create());
        harness.ViewModel.OpenPaths([source]);
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);

        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Details;
        harness.WaitFor(() => editor.Details.HasPrivateData);
        harness.Screenshot("details-panel");
        Assert.Contains(editor.Details.Fields, field => field.Group == "Location");

        await editor.Details.RemoveCommand.ExecuteAsync(null);
        harness.Pump();

        var output = Path.Combine(harness.Folder, "trip-clean.jpg");
        Assert.False(ImageMetadataInfo.Read(output).HasPrivateData);
        Assert.True(ImageMetadataInfo.Read(source).HasPrivateData);
        Assert.Contains("wasn't changed", editor.Details.ResultText);
    });

    [Fact]
    public void Resize_shows_an_estimated_size() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 800, 600);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Resize;
        editor.Resize.WidthText = "400";

        harness.WaitFor(() => editor.Resize.EstimateText.StartsWith("About"));
    });

    [Fact]
    public void Save_shortcuts_are_bound() => UiHarness.Run(harness =>
    {
        var bindings = harness.Window.InputBindings.OfType<KeyBinding>().ToList();

        Assert.Contains(bindings, binding => binding.Key == Key.S && binding.Modifiers == ModifierKeys.Control);
        Assert.Contains(bindings, binding => binding.Key == Key.S && binding.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift));
    });

    private static byte[] Noise(int width, int height, ImageFormat format)
    {
        var random = new Random(3);
        using var image = ImageBuffer.Create(width, height);
        var pixels = image.Pixels;
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = 0xFF000000 | ((uint)random.Next() & 0x00FFFFFF);
        }

        return ImageEncoder.Encode(image, new EncodeOptions(format, 90));
    }
}
