using System.IO;
using System.Windows.Input;
using PicKit.Core.Imaging;
using SkiaSharp;

namespace PicKit.UiTests;

public class ClipboardUiTests
{
    private static async Task WaitForCopy(UiHarness harness, ImageFormat? _ = null)
    {
        await Task.Yield();
        harness.WaitFor(() => harness.ViewModel.Editor.IsIdle);
        harness.Pump();
    }

    [Fact]
    public void Copy_puts_the_edited_image_on_the_clipboard_without_saving() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 400, 200);
        var editor = harness.ViewModel.Editor;
        await editor.Rotate.RotateRightCommand.ExecuteAsync(null);
        harness.Pump();

        await harness.ViewModel.CopyCommand.ExecuteAsync(null);
        await WaitForCopy(harness);

        using var pasted = harness.Clipboard.ReadImage()!;
        Assert.Equal((200, 400), (pasted.Width, pasted.Height));
        Assert.True(editor.Document!.IsDirty);
        Assert.Contains("Copied", harness.ViewModel.NoticeText);
    });

    [Fact]
    public void Copy_includes_markup_and_marked_redactions() => UiHarness.Run(async harness =>
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

        await harness.ViewModel.CopyCommand.ExecuteAsync(null);
        await WaitForCopy(harness);

        using var pasted = harness.Clipboard.ReadImage()!;
        Assert.Equal(0xFF000000, pasted.Pixels[(100 * 400) + 200]);
    });

    [Fact]
    public void Copy_as_png_keeps_transparency() => UiHarness.Run(async harness =>
    {
        var source = Path.Combine(harness.Folder, "logo.png");
        using (var image = ImageBuffer.Create(64, 64))
        {
            await File.WriteAllBytesAsync(source, ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png)));
        }

        harness.ViewModel.OpenPaths([source]);
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);

        await harness.ViewModel.CopyAsPngCommand.ExecuteAsync(null);
        await WaitForCopy(harness);

        using var pasted = harness.Clipboard.ReadImage()!;
        Assert.True(pasted.HasTransparency());
    });

    [Fact]
    public void Copy_as_jpeg_offers_jpeg() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 200, 100);

        await harness.ViewModel.CopyAsJpegCommand.ExecuteAsync(null);
        await WaitForCopy(harness);

        Assert.Contains(harness.Clipboard.Contents!, item => item.Format == ClipboardCodec.JpegFormat);
    });

    [Fact]
    public void A_locked_clipboard_says_so() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 200, 100);
        harness.Clipboard.IsLocked = true;

        await harness.ViewModel.CopyCommand.ExecuteAsync(null);
        await WaitForCopy(harness);

        Assert.Contains("Couldn't copy", Assert.Single(harness.Dialogs.Errors));
    });

    [Fact]
    public void Screenshot_on_the_clipboard_opens_without_saving_first() => UiHarness.Run(async harness =>
    {
        using (var shot = ImageBuffer.Create(320, 200))
        {
            shot.Bitmap.Erase(SKColors.SteelBlue);
            harness.Clipboard.Contents = ClipboardCodec.Encode(shot, CopyMode.Standard, SKColors.White);
        }

        harness.ViewModel.RefreshClipboardState();
        Assert.True(harness.ViewModel.CanPasteFromClipboard);
        harness.Screenshot("empty-with-paste");

        await harness.ViewModel.PasteCommand.ExecuteAsync(null);
        harness.WaitFor(() => harness.ViewModel.HasImage);
        harness.Pump();

        var document = harness.ViewModel.Editor.Document!;
        Assert.Null(document.Source.Path);
        Assert.Equal(320, document.Pixels.Width);
        Assert.Contains("Pasted image", harness.ViewModel.WindowTitle);
    });

    [Fact]
    public void Pasted_image_can_be_edited_and_copied_straight_back() => UiHarness.Run(async harness =>
    {
        using (var shot = ImageBuffer.Create(320, 200))
        {
            shot.Bitmap.Erase(SKColors.SteelBlue);
            harness.Clipboard.Contents = ClipboardCodec.Encode(shot, CopyMode.Standard, SKColors.White);
        }

        await harness.ViewModel.PasteCommand.ExecuteAsync(null);
        harness.WaitFor(() => harness.ViewModel.HasImage);
        var editor = harness.ViewModel.Editor;

        editor.ActiveTool = editor.Crop;
        harness.Pump();
        await editor.Crop.ApplyCommand.ExecuteAsync(null);
        await harness.ViewModel.CopyCommand.ExecuteAsync(null);
        await WaitForCopy(harness);

        using var pasted = harness.Clipboard.ReadImage()!;
        Assert.Equal(320, pasted.Width);
    });

    [Fact]
    public void Saving_a_pasted_image_asks_where_it_should_go() => UiHarness.Run(async harness =>
    {
        using (var shot = ImageBuffer.Create(32, 32))
        {
            shot.Bitmap.Erase(SKColors.SteelBlue);
            harness.Clipboard.Contents = ClipboardCodec.Encode(shot, CopyMode.Standard, SKColors.White);
        }

        await harness.ViewModel.PasteCommand.ExecuteAsync(null);
        harness.WaitFor(() => harness.ViewModel.HasImage);

        var target = Path.Combine(harness.Folder, "screenshot.png");
        harness.Dialogs.SavePath = target;
        await harness.ViewModel.SaveCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.True(File.Exists(target));
        Assert.Equal(target, harness.ViewModel.Editor.Document!.Source.Path);
    });

    [Fact]
    public void Pasting_files_from_explorer_opens_them() => UiHarness.Run(async harness =>
    {
        var path = Path.Combine(harness.Folder, "copied.png");
        using (var image = ImageBuffer.Create(40, 30))
        {
            await File.WriteAllBytesAsync(path, ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png)));
        }

        harness.Clipboard.Files = [path];

        await harness.ViewModel.PasteCommand.ExecuteAsync(null);
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);

        Assert.Equal(path, harness.ViewModel.Editor.Document!.Source.Path);
    });

    [Fact]
    public void Pasting_with_nothing_usable_says_so_and_keeps_the_image() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 100, 100);
        var document = harness.ViewModel.Editor.Document;

        await harness.ViewModel.PasteCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.Same(document, harness.ViewModel.Editor.Document);
        Assert.Contains("No image", harness.ViewModel.NoticeText);
    });

    [Fact]
    public void Paste_protects_unsaved_edits() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 400, 200);
        var editor = harness.ViewModel.Editor;
        await editor.Rotate.RotateRightCommand.ExecuteAsync(null);
        harness.Pump();

        using (var shot = ImageBuffer.Create(32, 32))
        {
            harness.Clipboard.Contents = ClipboardCodec.Encode(shot, CopyMode.Standard, SKColors.White);
        }

        var document = editor.Document;
        await harness.ViewModel.PasteCommand.ExecuteAsync(null);
        harness.Pump();

        // The paste gets its own tab, so the edits are still there and nothing had to be asked.
        Assert.Equal(0, harness.Dialogs.UnsavedPrompts);
        Assert.NotSame(document, editor.Document);
        Assert.Same(document, harness.ViewModel.Tabs[0].Document);
        Assert.True(document!.IsDirty);
    });

    [Theory]
    [InlineData(Key.C)]
    [InlineData(Key.V)]
    public void Clipboard_shortcuts_are_bound(Key key) => UiHarness.Run(harness =>
    {
        Assert.Contains(
            harness.Window.InputBindings.OfType<KeyBinding>(),
            binding => binding.Key == key && binding.Modifiers == ModifierKeys.Control);
    });
}
