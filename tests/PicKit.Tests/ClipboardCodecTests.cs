using System.Buffers.Binary;
using PicKit.Core.Imaging;
using SkiaSharp;

namespace PicKit.Tests;

public class ClipboardCodecTests
{
    private static ImageBuffer HalfTransparent()
    {
        var image = TestImages.Solid(8, 6, SKColors.Transparent);
        using var canvas = image.CreateCanvas();
        using var paint = new SKPaint { Color = new SKColor(200, 40, 20, 128) };
        canvas.DrawRect(0, 0, 4, 6, paint);
        return image;
    }

    private static Func<string, byte[]?> Reader(IEnumerable<ClipboardItem> items, params string[] available)
    {
        var map = items.ToDictionary(item => item.Format, item => item.Data);
        return format => (available.Length == 0 || available.Contains(format)) && map.TryGetValue(format, out var data) ? data : null;
    }

    [Fact]
    public void Standard_copy_offers_every_format_apps_look_for()
    {
        using var image = TestImages.Photo(32, 24);

        var formats = ClipboardCodec.Encode(image, CopyMode.Standard, SKColors.White).Select(item => item.Format).ToList();

        Assert.Equal([ClipboardCodec.PngFormat, ClipboardCodec.DibV5Format, ClipboardCodec.DibFormat], formats);
    }

    [Fact]
    public void Png_item_is_a_real_png_that_keeps_transparency()
    {
        using var image = HalfTransparent();

        var png = ClipboardCodec.Encode(image, CopyMode.Png, SKColors.White).Single(item => item.Format == ClipboardCodec.PngFormat);

        using var decoded = ImageDecoder.Decode(png.Data);
        Assert.Equal(ImageFormat.Png, decoded.Format);
        Assert.True(decoded.Pixels.HasTransparency());
    }

    [Fact]
    public void Jpeg_copy_is_a_real_jpeg_and_flattens_for_older_apps()
    {
        using var image = HalfTransparent();

        var items = ClipboardCodec.Encode(image, CopyMode.Jpeg, SKColors.White);

        using var jpeg = ImageDecoder.Decode(items.Single(item => item.Format == ClipboardCodec.JpegFormat).Data);
        Assert.Equal(ImageFormat.Jpeg, jpeg.Format);
        Assert.Contains(items, item => item.Format == ClipboardCodec.DibFormat);
    }

    [Fact]
    public void Flat_dib_never_shows_transparency_as_black()
    {
        using var image = TestImages.Solid(4, 4, SKColors.Transparent);

        var dib = ClipboardCodec.ToFlatDib(image, SKColors.White);

        Assert.Equal(24, BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14)));
        Assert.All(dib.Skip(40).Take(12).ToArray(), value => Assert.Equal(255, value));
    }

    [Fact]
    public void Flat_dib_pads_rows_to_four_bytes()
    {
        using var image = TestImages.Solid(5, 3, SKColors.Red);

        var dib = ClipboardCodec.ToFlatDib(image, SKColors.White);

        // Five pixels at three bytes each is 15, which pads to 16 per row.
        Assert.Equal(40 + (16 * 3), dib.Length);
    }

    [Fact]
    public void DibV5_header_declares_alpha_and_srgb()
    {
        using var image = HalfTransparent();

        var dib = ClipboardCodec.ToDibV5(image);

        Assert.Equal(124u, BinaryPrimitives.ReadUInt32LittleEndian(dib));
        Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14)));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(16)));
        Assert.Equal(0xFF000000u, BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(52)));
        Assert.Equal(0x73524742u, BinaryPrimitives.ReadUInt32LittleEndian(dib.AsSpan(56)));
    }

    [Fact]
    public void Dib_round_trip_is_exact_and_right_way_up()
    {
        using var source = TestImages.Numbered(13, 7);

        using var decoded = ClipboardCodec.Decode(
            Reader(ClipboardCodec.Encode(source, CopyMode.Standard, SKColors.White), ClipboardCodec.DibFormat))!;

        Assert.True(source.Pixels.SequenceEqual(decoded.Pixels));
    }

    [Fact]
    public void DibV5_round_trip_keeps_transparency()
    {
        using var source = HalfTransparent();

        using var decoded = ClipboardCodec.Decode(
            Reader(ClipboardCodec.Encode(source, CopyMode.Standard, SKColors.White), ClipboardCodec.DibV5Format))!;

        Assert.True(decoded.HasTransparency());
        Assert.Equal(0u, decoded.Pixels[7] >> 24);
        Assert.InRange(decoded.Pixels[0] >> 24, 126u, 130u);
    }

    [Fact]
    public void Png_is_preferred_when_several_formats_are_offered()
    {
        using var source = HalfTransparent();
        var asked = new List<string>();
        var items = ClipboardCodec.Encode(source, CopyMode.Standard, SKColors.White).ToDictionary(item => item.Format, item => item.Data);

        using var decoded = ClipboardCodec.Decode(format =>
        {
            asked.Add(format);
            return items.GetValueOrDefault(format);
        });

        Assert.Equal(ClipboardCodec.PngFormat, asked[0]);
        Assert.Single(asked);
    }

    [Fact]
    public void Screenshot_style_bitmaps_with_zero_alpha_open_as_opaque()
    {
        // 32-bit DIBs from screen capture leave the alpha byte at zero throughout.
        const int width = 2;
        const int height = 2;
        var dib = new byte[40 + (width * height * 4)];
        BinaryPrimitives.WriteUInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);
        dib[40] = 10;
        dib[41] = 20;
        dib[42] = 30;

        using var decoded = ClipboardCodec.Decode(format => format == ClipboardCodec.DibFormat ? dib : null)!;

        Assert.False(decoded.HasTransparency());

        // Bottom-up: the first stored pixel is the bottom-left of the picture.
        Assert.Equal(0xFF1E140Au, decoded.Pixels[2]);
    }

    [Fact]
    public void Top_down_bitmaps_are_not_flipped()
    {
        var dib = new byte[40 + 8];
        BinaryPrimitives.WriteUInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), -2);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);
        dib[40] = 200;

        using var decoded = ClipboardCodec.Decode(format => format == ClipboardCodec.DibFormat ? dib : null)!;

        Assert.Equal(200u, decoded.Pixels[0] & 0xFF);
    }

    [Fact]
    public void Paletted_bitmaps_still_open()
    {
        // 2 x 1, one bit per pixel, with a black and white palette.
        var dib = new byte[40 + 8 + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 1);
        dib[44] = 0xFF;
        dib[45] = 0xFF;
        dib[46] = 0xFF;
        dib[48] = 0b0100_0000;

        using var decoded = ClipboardCodec.Decode(format => format == ClipboardCodec.DibFormat ? dib : null);

        Assert.NotNull(decoded);
        Assert.Equal((2, 1), (decoded.Width, decoded.Height));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(40)]
    [InlineData(41)]
    public void Damaged_clipboard_data_is_ignored_safely(int length)
    {
        var junk = new byte[length];
        new Random(5).NextBytes(junk);

        Assert.Null(ClipboardCodec.Decode(format => format == ClipboardCodec.DibFormat ? junk : null));
    }

    [Fact]
    public void Truncated_bitmap_with_a_huge_claimed_size_is_refused()
    {
        var dib = new byte[64];
        BinaryPrimitives.WriteUInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), 60000);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), 60000);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(dib.AsSpan(14), 32);

        Assert.Null(ClipboardCodec.Decode(format => format == ClipboardCodec.DibFormat ? dib : null));
    }

    [Fact]
    public void An_empty_clipboard_gives_nothing()
    {
        Assert.Null(ClipboardCodec.Decode(_ => null));
    }
}
