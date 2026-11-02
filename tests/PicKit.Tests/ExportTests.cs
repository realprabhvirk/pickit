using PicKit.Core.Documents;
using PicKit.Core.Files;
using PicKit.Core.Imaging;
using SkiaSharp;

namespace PicKit.Tests;

public class ExportTests
{
    [Theory]
    [InlineData(@"C:\pics\photo.jpg", "edited", ImageFormat.Jpeg, "photo-edited.jpg")]
    [InlineData(@"C:\pics\photo.jpeg", "compressed", ImageFormat.Jpeg, "photo-compressed.jpeg")]
    [InlineData(@"C:\pics\photo.jpg", "webp", ImageFormat.WebP, "photo-webp.webp")]
    [InlineData(@"C:\pics\photo.jpg", "transparent", ImageFormat.Png, "photo-transparent.png")]
    public void Output_names_are_sensible_and_correctly_extended(string source, string suffix, ImageFormat format, string expected)
    {
        Assert.Equal(expected, Path.GetFileName(ExportService.OutputPathFor(source, suffix, format)));
    }

    [Fact]
    public void Output_names_never_collide_with_existing_files()
    {
        var folder = TestImages.TempFolder();
        var source = Path.Combine(folder, "photo.jpg");
        File.WriteAllBytes(Path.Combine(folder, "photo-edited.jpg"), [1]);
        File.WriteAllBytes(Path.Combine(folder, "photo-edited (2).jpg"), [1]);

        var path = ExportService.OutputPathFor(source, "edited", ImageFormat.Jpeg);

        Assert.Equal("photo-edited (3).jpg", Path.GetFileName(path));
    }

    [Fact]
    public void Save_writes_a_valid_file()
    {
        var path = Path.Combine(TestImages.TempFolder(), "out.png");
        using var image = TestImages.Photo(50, 40);

        var result = ExportService.Save(image, new EncodeOptions(ImageFormat.Png), path);

        Assert.Equal(new FileInfo(path).Length, result.Size);
        using var decoded = ImageDecoder.Decode(path);
        Assert.True(image.Pixels.SequenceEqual(decoded.Pixels.Pixels));
    }

    [Fact]
    public void A_failed_write_leaves_the_original_intact()
    {
        var path = Path.Combine(TestImages.TempFolder(), "photo.jpg");
        File.WriteAllBytes(path, [1, 2, 3, 4]);

        Assert.ThrowsAny<Exception>(() => SafeFile.Write(path, stream =>
        {
            stream.WriteByte(9);
            throw new IOException("Disk full.");
        }));

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(path));
    }

    [Fact]
    public void A_failed_write_leaves_no_temporary_files_behind()
    {
        var folder = TestImages.TempFolder();
        var path = Path.Combine(folder, "photo.jpg");

        Assert.ThrowsAny<Exception>(() => SafeFile.Write(path, _ => throw new IOException("Disk full.")));

        Assert.Empty(Directory.GetFiles(folder));
    }

    [Fact]
    public void Writing_to_a_locked_file_gives_a_friendly_error_and_keeps_it()
    {
        var path = Path.Combine(TestImages.TempFolder(), "locked.png");
        File.WriteAllBytes(path, [1, 2, 3]);
        using var image = TestImages.Photo(10, 10);

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = Assert.Throws<ImageException>(() =>
                ExportService.Save(image, new EncodeOptions(ImageFormat.Png), path));

            Assert.Contains("couldn't save", error.Message);
        }

        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData("holiday photo", "holiday photo")]
    [InlineData("a/b\\c:d*e?f\"g<h>i|j", "a_b_c_d_e_f_g_h_i_j")]
    [InlineData("trailing dots...", "trailing dots")]
    [InlineData("   ", "image")]
    [InlineData("CON", "_CON")]
    [InlineData("nul.txt", "_nul.txt")]
    [InlineData("..", "image")]
    public void Untrusted_names_are_made_safe(string input, string expected)
    {
        Assert.Equal(expected, FileNames.Sanitize(input));
    }

    [Fact]
    public void Sanitising_blocks_path_traversal()
    {
        var clean = FileNames.Sanitize(@"..\..\Windows\System32\evil.exe");

        Assert.DoesNotContain("\\", clean);
        Assert.Equal(clean, Path.GetFileName(clean));
    }

    [Fact]
    public void Very_long_names_are_trimmed()
    {
        Assert.True(FileNames.Sanitize(new string('a', 400)).Length <= 240);
    }

    [Fact]
    public void Exported_redaction_contains_none_of_the_hidden_pixels()
    {
        var path = Path.Combine(TestImages.TempFolder(), "redacted.png");
        using var document = new EditorDocument(TestImages.Numbered(200, 200), new DocumentSource(null, ImageFormat.Png, 0));
        using var original = document.Pixels.Clone();
        var region = new SKRectI(50, 50, 150, 150);

        using (var edit = RegionEdit.Prepare(
            document.Pixels, document.Markups, [region], (_, area) => ImageOps.SolidRegion(area.Width, area.Height, SKColors.Black)))
        {
            document.ApplyRegionEdit("Redact", edit);
        }

        using (var flattened = document.RenderFlattened())
        {
            ExportService.Save(flattened, new EncodeOptions(ImageFormat.Png), path);
        }

        // The undo history still holds the pixels in memory, but the file on disk must not.
        using var saved = ImageDecoder.Decode(path);
        var inFile = saved.Pixels.Pixels.ToArray().ToHashSet();
        for (var y = region.Top; y < region.Bottom; y++)
        {
            for (var x = region.Left; x < region.Right; x++)
            {
                Assert.DoesNotContain(original.Pixels[(y * 200) + x], inFile);
            }
        }
    }
}
