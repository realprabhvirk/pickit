using PicKit.Core.Imaging;
using PicKit.Core.Metadata;
using SkiaSharp;

namespace PicKit.Tests;

/// <summary>Image files are untrusted input. Damaged ones must fail politely, never crash.</summary>
public class HostileInputTests
{
    private static readonly int Rounds = int.Parse(Environment.GetEnvironmentVariable("PICKIT_FUZZ_ROUNDS") ?? "150");

    public static TheoryData<ImageFormat> Formats => [ImageFormat.Jpeg, ImageFormat.Png, ImageFormat.WebP];

    private static byte[] Sample(ImageFormat format)
    {
        using var image = TestImages.Photo(96, 64);
        var data = ImageEncoder.Encode(image, new EncodeOptions(format, 85));
        return format switch
        {
            ImageFormat.Jpeg => ExifFixture.WithJpegMetadata(data),
            ImageFormat.Png => ExifFixture.WithPngMetadata(data),
            _ => ExifFixture.WithWebPMetadata(data, 96, 64),
        };
    }

    /// <summary>Only friendly errors are allowed through, and any image returned must be usable.</summary>
    private static void AssertHandled(byte[] data)
    {
        try
        {
            using var decoded = ImageDecoder.Decode(data);
            Assert.True(decoded.Pixels.Width > 0);
        }
        catch (ImageException)
        {
            // The expected way for a bad file to fail.
        }

        _ = ImageMetadataInfo.Read(data);
        var stripped = MetadataStripper.Strip(data);
        if (stripped is not null)
        {
            Assert.False(ExifFixture.ContainsSecret(stripped), "Metadata survived in a file reported as stripped.");
        }
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void Random_bit_damage_never_crashes(ImageFormat format)
    {
        var original = Sample(format);
        var random = new Random(1234);

        for (var round = 0; round < Rounds; round++)
        {
            var damaged = (byte[])original.Clone();
            var flips = random.Next(1, 40);
            for (var i = 0; i < flips; i++)
            {
                damaged[random.Next(damaged.Length)] ^= (byte)(1 << random.Next(8));
            }

            AssertHandled(damaged);
        }
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void Files_cut_short_at_any_point_never_crash(ImageFormat format)
    {
        var original = Sample(format);
        var step = Math.Max(1, original.Length / Rounds);

        for (var length = 0; length < original.Length; length += step)
        {
            AssertHandled(original.AsSpan(0, length).ToArray());
        }
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void Damaged_headers_never_crash(ImageFormat format)
    {
        var original = Sample(format);
        var random = new Random(99);

        // The first bytes hold sizes and offsets, the fields attackers aim for.
        for (var round = 0; round < Rounds; round++)
        {
            var damaged = (byte[])original.Clone();
            for (var i = 0; i < 8; i++)
            {
                damaged[random.Next(Math.Min(96, damaged.Length))] = (byte)random.Next(256);
            }

            AssertHandled(damaged);
        }
    }

    [Fact]
    public void Exif_with_a_directory_pointing_outside_the_file_is_survived()
    {
        var tiff = ExifFixture.Tiff();
        tiff[4] = 0xFF;
        tiff[5] = 0xFF;
        tiff[6] = 0xFF;
        tiff[7] = 0x7F;

        using var image = TestImages.Photo(32, 32);
        var jpeg = ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Jpeg));
        var segment = new byte[10 + tiff.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        segment[2] = (byte)((tiff.Length + 8) >> 8);
        segment[3] = (byte)(tiff.Length + 8);
        "Exif\0\0"u8.CopyTo(segment.AsSpan(4));
        tiff.CopyTo(segment, 10);
        var hostile = ExifCarryOver.Insert(jpeg, segment);

        AssertHandled(hostile);
        Assert.Null(ExifCarryOver.ExtractUprightExif(hostile));
    }

    [Fact]
    public void Animated_images_report_their_frames_and_show_the_first()
    {
        // A minimal two-frame GIF, one pixel each.
        byte[] gif =
        [
            0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
            0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF,
            0x21, 0xFF, 0x0B, 0x4E, 0x45, 0x54, 0x53, 0x43, 0x41, 0x50, 0x45, 0x32, 0x2E, 0x30, 0x03, 0x01, 0x00, 0x00, 0x00,
            0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x44, 0x01, 0x00,
            0x21, 0xF9, 0x04, 0x00, 0x0A, 0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x4C, 0x01, 0x00,
            0x3B,
        ];

        using var decoded = ImageDecoder.Decode(gif);

        Assert.Equal(ImageFormat.Gif, decoded.Format);
        Assert.True(decoded.IsAnimated);
        Assert.Equal(2, decoded.FrameCount);
        Assert.Equal(0xFFFF0000, decoded.Pixels.Pixels[0]);
    }

    [Theory]
    [InlineData(SKEncodedOrigin.TopRight, 120, 80)]
    [InlineData(SKEncodedOrigin.BottomRight, 120, 80)]
    [InlineData(SKEncodedOrigin.RightTop, 80, 120)]
    [InlineData(SKEncodedOrigin.LeftBottom, 80, 120)]
    public void Camera_rotation_is_honoured_when_opening(SKEncodedOrigin origin, int width, int height)
    {
        using var image = TestImages.Photo(120, 80);
        var jpeg = ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Jpeg));
        var tagged = ExifFixture.WithJpegMetadata(jpeg, (ushort)origin);

        using var decoded = ImageDecoder.Decode(tagged);

        Assert.Equal((width, height), (decoded.Pixels.Width, decoded.Pixels.Height));
    }
}
