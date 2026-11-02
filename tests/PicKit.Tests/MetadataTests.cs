using PicKit.Core.Files;
using PicKit.Core.Imaging;
using PicKit.Core.Metadata;
using SkiaSharp;

namespace PicKit.Tests;

public class MetadataTests
{
    private static byte[] Jpeg(int width = 120, int height = 80) =>
        TestEncode(TestImages.Photo(width, height), ImageFormat.Jpeg);

    private static byte[] Png() => TestEncode(TestImages.Photo(60, 40), ImageFormat.Png);

    private static byte[] WebP() => TestEncode(TestImages.Photo(60, 40), ImageFormat.WebP);

    private static byte[] TestEncode(ImageBuffer image, ImageFormat format)
    {
        using (image)
        {
            return ImageEncoder.Encode(image, new EncodeOptions(format));
        }
    }

    [Fact]
    public void Reads_camera_and_location_from_exif()
    {
        var info = ImageMetadataInfo.Read(ExifFixture.WithJpegMetadata(Jpeg()));

        Assert.True(info.HasPrivateData);
        Assert.True(info.HasLocation);
        Assert.Contains(info.Fields, field => field.Name == "Make" && field.Value == ExifFixture.SecretMake);
        Assert.Contains(info.Fields, field => field.Name == "GPS" && field.Value.StartsWith("-27.0"));
    }

    [Fact]
    public void Plain_image_reports_no_private_data()
    {
        var info = ImageMetadataInfo.Read(Png());

        Assert.False(info.HasPrivateData);
        Assert.Empty(info.Fields);
    }

    [Fact]
    public void Jpeg_stripping_removes_everything_private()
    {
        var dirty = ExifFixture.WithJpegMetadata(Jpeg());
        Assert.True(ExifFixture.ContainsSecret(dirty));

        var clean = MetadataStripper.Strip(dirty)!;

        Assert.False(ExifFixture.ContainsSecret(clean));
        Assert.False(ImageMetadataInfo.Read(clean).HasPrivateData);
    }

    [Fact]
    public void Jpeg_stripping_does_not_touch_the_image_data()
    {
        var original = Jpeg();

        var clean = MetadataStripper.Strip(ExifFixture.WithJpegMetadata(original))!;

        using var expected = ImageDecoder.Decode(original);
        using var actual = ImageDecoder.Decode(clean);
        Assert.True(expected.Pixels.Pixels.SequenceEqual(actual.Pixels.Pixels));
        Assert.Equal(original.Length, clean.Length);
    }

    [Fact]
    public void Data_hidden_after_the_end_of_a_jpeg_is_removed()
    {
        var dirty = ExifFixture.WithJpegMetadata(Jpeg()).Concat(System.Text.Encoding.ASCII.GetBytes(ExifFixture.SecretComment)).ToArray();

        var clean = MetadataStripper.Strip(dirty)!;

        Assert.False(ExifFixture.ContainsSecret(clean));
    }

    [Fact]
    public void Png_stripping_removes_text_exif_and_timestamps()
    {
        var dirty = ExifFixture.WithPngMetadata(Png());
        Assert.True(ImageMetadataInfo.Read(dirty).HasPrivateData);

        var clean = MetadataStripper.Strip(dirty)!;

        Assert.False(ExifFixture.ContainsSecret(clean));
        Assert.False(ExifFixture.Contains(clean, "tIME"));
        Assert.False(ImageMetadataInfo.Read(clean).HasPrivateData);

        using var expected = ImageDecoder.Decode(Png());
        using var actual = ImageDecoder.Decode(clean);
        Assert.True(expected.Pixels.Pixels.SequenceEqual(actual.Pixels.Pixels));
    }

    [Fact]
    public void WebP_stripping_removes_metadata_and_stays_decodable()
    {
        var dirty = ExifFixture.WithWebPMetadata(WebP(), 60, 40);
        Assert.True(ExifFixture.ContainsSecret(dirty));

        var clean = MetadataStripper.Strip(dirty)!;

        Assert.False(ExifFixture.ContainsSecret(clean));
        using var decoded = ImageDecoder.Decode(clean);
        Assert.Equal((60, 40), (decoded.Pixels.Width, decoded.Pixels.Height));

        // The extended header must no longer claim metadata that isn't there.
        Assert.Equal(0, clean[20] & 0x0C);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE1, 0xFF, 0xFF, 0x00 })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFF, 0xFF, 0xFF, 0xFF, 0x49, 0x44, 0x41, 0x54 })]
    public void Malformed_files_are_declined_rather_than_half_cleaned(byte[] file)
    {
        Assert.Null(MetadataStripper.Strip(file));
    }

    [Fact]
    public void Clean_export_is_truly_clean_and_stays_lossless_when_it_can()
    {
        var folder = TestImages.TempFolder();
        var source = Path.Combine(folder, "photo.jpg");
        var target = Path.Combine(folder, "photo-clean.jpg");
        var original = Jpeg();
        File.WriteAllBytes(source, ExifFixture.WithJpegMetadata(original));

        ExportService.WriteClean(source, target);

        var clean = File.ReadAllBytes(target);
        Assert.False(ExifFixture.ContainsSecret(clean));
        Assert.False(ImageMetadataInfo.Read(target).HasPrivateData);
        Assert.Equal(original.Length, clean.Length);
    }

    [Fact]
    public void Removing_metadata_does_not_turn_a_rotated_photo_sideways()
    {
        var folder = TestImages.TempFolder();
        var source = Path.Combine(folder, "photo.jpg");
        var target = Path.Combine(folder, "photo-clean.jpg");

        // Orientation 6: stored landscape, meant to be shown portrait.
        File.WriteAllBytes(source, ExifFixture.WithJpegMetadata(Jpeg(120, 80), orientation: 6));

        ExportService.WriteClean(source, target);

        using var decoded = ImageDecoder.Decode(target);
        Assert.Equal((80, 120), (decoded.Pixels.Width, decoded.Pixels.Height));
        Assert.False(ExifFixture.ContainsSecret(File.ReadAllBytes(target)));
        Assert.False(ImageMetadataInfo.Read(target).HasPrivateData);
    }

    [Fact]
    public void Formats_without_a_container_rewrite_fall_back_to_pixels()
    {
        Assert.Null(MetadataStripper.Strip([0x42, 0x4D, 0, 0, 0, 0]));
    }

    [Fact]
    public void Carrying_exif_resets_orientation_so_photos_are_not_turned_twice()
    {
        var dirty = ExifFixture.WithJpegMetadata(Jpeg(120, 80), orientation: 6);
        using var upright = ImageDecoder.Decode(dirty);
        Assert.Equal(80, upright.Pixels.Width);

        var segment = ExifCarryOver.ExtractUprightExif(dirty)!;
        var encoded = ImageEncoder.Encode(upright.Pixels, new EncodeOptions(ImageFormat.Jpeg));
        var kept = ExifCarryOver.Insert(encoded, segment);

        using var reopened = ImageDecoder.Decode(kept);
        Assert.Equal((80, 120), (reopened.Pixels.Width, reopened.Pixels.Height));
        Assert.Contains(ImageMetadataInfo.Read(kept).Fields, field => field.Value == ExifFixture.SecretMake);
    }

    [Fact]
    public void Files_saved_by_pickit_carry_no_metadata()
    {
        using var decoded = ImageDecoder.Decode(ExifFixture.WithJpegMetadata(Jpeg()));

        var saved = ImageEncoder.Encode(decoded.Pixels, new EncodeOptions(ImageFormat.Jpeg));

        Assert.False(ExifFixture.ContainsSecret(saved));
        Assert.False(ImageMetadataInfo.Read(saved).HasPrivateData);
    }
}
