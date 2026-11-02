using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PicKit.Core.Imaging;

namespace PicKit.UiTests;

public class FormatUiTests
{
    private static byte[] TiffBytes()
    {
        var pixels = new byte[32 * 16 * 4];
        Array.Fill(pixels, (byte)200);
        var source = BitmapSource.Create(32, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 32 * 4);
        var encoder = new TiffBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    [Fact]
    public void Tiff_opens_through_the_windows_codec_and_saves_as_another_format() => UiHarness.Run(async harness =>
    {
        var path = Path.Combine(harness.Folder, "scan.tiff");
        await File.WriteAllBytesAsync(path, TiffBytes());

        harness.ViewModel.OpenPaths([path]);
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);

        var document = harness.ViewModel.Editor.Document!;
        Assert.Equal(ImageFormat.Tiff, document.Source.Format);
        Assert.Equal(32, document.Pixels.Width);

        // PicKit can't write TIFF, so Save must ask where to put a format it can write.
        var target = Path.Combine(harness.Folder, "scan.png");
        harness.Dialogs.SavePath = target;
        await harness.ViewModel.SaveCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.True(File.Exists(target));
        Assert.Empty(harness.Dialogs.Confirmations);
    });

    [Fact]
    public void A_corrupt_file_shows_an_error_and_leaves_the_open_image() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 100, 80);
        var document = harness.ViewModel.Editor.Document;
        var broken = Path.Combine(harness.Folder, "broken.jpg");
        await File.WriteAllBytesAsync(broken, [0xFF, 0xD8, 0xFF, 1, 2, 3, 4]);

        harness.ViewModel.OpenPaths([broken]);
        harness.WaitFor(() => harness.Dialogs.Errors.Count > 0);
        harness.Pump();

        Assert.Equal("Couldn't open image", Assert.Single(harness.Dialogs.Errors));
        Assert.Same(document, harness.ViewModel.Editor.Document);
    });

    [Fact]
    public void A_cut_off_photo_opens_with_a_warning() => UiHarness.Run(async harness =>
    {
        using var image = ImageBuffer.Create(200, 150);
        image.Bitmap.Erase(SkiaSharp.SKColors.SteelBlue);
        var jpeg = ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Jpeg, 95));
        var path = Path.Combine(harness.Folder, "cut.jpg");
        await File.WriteAllBytesAsync(path, jpeg.AsSpan(0, jpeg.Length * 2 / 3).ToArray());

        harness.ViewModel.OpenPaths([path]);
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);

        Assert.Contains("incomplete", Assert.Single(harness.Dialogs.Errors));
    });

    [Fact]
    public void Unicode_and_spaces_in_paths_work_throughout() => UiHarness.Run(async harness =>
    {
        var folder = Directory.CreateDirectory(Path.Combine(harness.Folder, "Fotos de férias 日本")).FullName;
        var path = Path.Combine(folder, "praia à noite.png");
        using (var image = ImageBuffer.Create(64, 48))
        {
            await File.WriteAllBytesAsync(path, ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png)));
        }

        harness.ViewModel.OpenPaths([path]);
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);
        await harness.ViewModel.SaveCopyCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.True(File.Exists(Path.Combine(folder, "praia à noite-edited.png")));
    });
}
