using System.Buffers.Binary;
using PicKit.Core.Diagnostics;
using SkiaSharp;

namespace PicKit.Core.Imaging;

public enum CopyMode
{
    /// <summary>Every format, so each app can pick the best one it understands.</summary>
    Standard,

    /// <summary>Lossless with transparency.</summary>
    Png,

    /// <summary>Compact and opaque.</summary>
    Jpeg,
}

/// <summary>One representation of an image on the clipboard.</summary>
public sealed record ClipboardItem(string Format, byte[] Data);

/// <summary>
/// Turns images into the bytes Windows apps expect on the clipboard, and back. There is no
/// clipboard access here, which keeps the fiddly format work fully testable.
/// </summary>
public static class ClipboardCodec
{
    public const string PngFormat = "PNG";
    public const string JpegFormat = "JFIF";
    public const string DibFormat = "CF_DIB";
    public const string DibV5Format = "CF_DIBV5";

    private const int BitmapInfoHeaderSize = 40;
    private const int BitmapV5HeaderSize = 124;
    private const int BitmapFileHeaderSize = 14;

    private const uint CompressionRgb = 0;
    private const uint CompressionBitFields = 3;
    private const uint ColorSpaceSrgb = 0x73524742;

    private const uint RedMask = 0x00FF0000;
    private const uint GreenMask = 0x0000FF00;
    private const uint BlueMask = 0x000000FF;
    private const uint AlphaMask = 0xFF000000;

    private const int JpegCopyQuality = 92;

    public static IReadOnlyList<ClipboardItem> Encode(ImageBuffer image, CopyMode mode, SKColor background)
    {
        switch (mode)
        {
            case CopyMode.Jpeg:
                var jpeg = ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Jpeg, JpegCopyQuality, background));
                return [new ClipboardItem(JpegFormat, jpeg), new ClipboardItem(DibFormat, ToFlatDib(image, background))];

            case CopyMode.Png:
                return [PngItem(image), new ClipboardItem(DibV5Format, ToDibV5(image))];

            default:
                return
                [
                    PngItem(image),
                    new ClipboardItem(DibV5Format, ToDibV5(image)),
                    new ClipboardItem(DibFormat, ToFlatDib(image, background)),
                ];
        }
    }

    /// <summary>Reads the best image available. <paramref name="read"/> returns a format's bytes, or null.</summary>
    public static ImageBuffer? Decode(Func<string, byte[]?> read)
    {
        foreach (var format in new[] { PngFormat, JpegFormat })
        {
            if (read(format) is { Length: > 0 } encoded && TryDecodeFile(encoded) is { } image)
            {
                return image;
            }
        }

        foreach (var format in new[] { DibV5Format, DibFormat })
        {
            if (read(format) is { Length: > BitmapInfoHeaderSize } dib && TryDecodeDib(dib) is { } image)
            {
                return image;
            }
        }

        return null;
    }

    /// <summary>A device-independent bitmap with straight alpha, as alpha-aware apps expect.</summary>
    public static byte[] ToDibV5(ImageBuffer image)
    {
        var width = image.Width;
        var height = image.Height;
        var stride = width * 4;
        var result = new byte[BitmapV5HeaderSize + (stride * height)];
        var header = result.AsSpan(0, BitmapV5HeaderSize);

        Write32(header, 0, BitmapV5HeaderSize);
        Write32(header, 4, (uint)width);
        Write32(header, 8, (uint)height);
        Write16(header, 12, 1);
        Write16(header, 14, 32);
        Write32(header, 16, CompressionBitFields);
        Write32(header, 20, (uint)(stride * height));
        Write32(header, 40, RedMask);
        Write32(header, 44, GreenMask);
        Write32(header, 48, BlueMask);
        Write32(header, 52, AlphaMask);
        Write32(header, 56, ColorSpaceSrgb);

        using var straight = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        if (!image.Bitmap.PeekPixels().ReadPixels(straight.PeekPixels()))
        {
            throw new ImageException(ImageErrorKind.EncodeFailed, "PicKit couldn't prepare this image for the clipboard.");
        }

        CopyRowsBottomUp(straight.GetPixelSpan(), stride, stride, height, result.AsSpan(BitmapV5HeaderSize));
        return result;
    }

    /// <summary>A plain 24-bit bitmap for older apps, so they never show transparency as black.</summary>
    public static byte[] ToFlatDib(ImageBuffer image, SKColor background)
    {
        using var flat = ImageOps.Flatten(image, background);

        var width = flat.Width;
        var height = flat.Height;
        var stride = ((width * 3) + 3) & ~3;
        var result = new byte[BitmapInfoHeaderSize + (stride * height)];

        var header = result.AsSpan(0, BitmapInfoHeaderSize);
        Write32(header, 0, BitmapInfoHeaderSize);
        Write32(header, 4, (uint)width);
        Write32(header, 8, (uint)height);
        Write16(header, 12, 1);
        Write16(header, 14, 24);
        Write32(header, 16, CompressionRgb);
        Write32(header, 20, (uint)(stride * height));

        var pixels = flat.Pixels;
        var body = result.AsSpan(BitmapInfoHeaderSize);
        for (var y = 0; y < height; y++)
        {
            var row = body.Slice((height - 1 - y) * stride, width * 3);
            var source = pixels.Slice(y * width, width);
            for (var x = 0; x < width; x++)
            {
                var pixel = source[x];
                row[x * 3] = (byte)pixel;
                row[(x * 3) + 1] = (byte)(pixel >> 8);
                row[(x * 3) + 2] = (byte)(pixel >> 16);
            }
        }

        return result;
    }

    private static ClipboardItem PngItem(ImageBuffer image) =>
        new(PngFormat, ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png)));

    private static ImageBuffer? TryDecodeFile(byte[] data)
    {
        try
        {
            return ImageDecoder.Decode(data).Pixels;
        }
        catch (ImageException ex)
        {
            Log.Info($"Clipboard image could not be decoded: {ex.Message}");
            return null;
        }
    }

    private static ImageBuffer? TryDecodeDib(byte[] dib)
    {
        try
        {
            return DecodeDirect(dib) ?? TryDecodeFile(WrapAsBmp(dib));
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or ImageException)
        {
            Log.Info($"Clipboard bitmap could not be decoded: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reads the common 24 and 32-bit layouts directly. Screenshots usually arrive as 32-bit
    /// with every alpha byte zero, which has to mean "opaque" rather than "invisible".
    /// </summary>
    private static ImageBuffer? DecodeDirect(byte[] dib)
    {
        var headerSize = (int)Read32(dib, 0);
        var width = (int)Read32(dib, 4);
        var signedHeight = (int)Read32(dib, 8);
        var bits = Read16(dib, 14);
        var compression = Read32(dib, 16);

        var height = Math.Abs(signedHeight);
        if (bits is not (24 or 32) || !ImageLimits.Fits(width, height))
        {
            return null;
        }

        var offset = headerSize;
        if (compression == CompressionBitFields)
        {
            var masksInHeader = headerSize > BitmapInfoHeaderSize;
            var masksAt = masksInHeader ? BitmapInfoHeaderSize : headerSize;
            if (Read32(dib, masksAt) != RedMask || Read32(dib, masksAt + 4) != GreenMask || Read32(dib, masksAt + 8) != BlueMask)
            {
                return null;
            }

            offset += masksInHeader ? 0 : 12;
        }
        else if (compression != CompressionRgb)
        {
            return null;
        }

        var stride = bits == 32 ? width * 4 : ((width * 3) + 3) & ~3;
        if (offset + ((long)stride * height) > dib.Length)
        {
            return null;
        }

        var hasAlpha = bits == 32 && HasAnyAlpha(dib, offset, stride, width, height);
        var image = ImageBuffer.Create(width, height);
        var pixels = image.Pixels;
        for (var y = 0; y < height; y++)
        {
            var sourceRow = signedHeight > 0 ? height - 1 - y : y;
            var row = dib.AsSpan(offset + (sourceRow * stride), stride);
            var target = pixels.Slice(y * width, width);
            for (var x = 0; x < width; x++)
            {
                target[x] = bits == 32 ? ReadPixel32(row, x, hasAlpha) : ReadPixel24(row, x);
            }
        }

        return image;
    }

    private static uint ReadPixel24(ReadOnlySpan<byte> row, int x) =>
        0xFF000000 | ((uint)row[(x * 3) + 2] << 16) | ((uint)row[(x * 3) + 1] << 8) | row[x * 3];

    private static uint ReadPixel32(ReadOnlySpan<byte> row, int x, bool hasAlpha)
    {
        var blue = (uint)row[x * 4];
        var green = (uint)row[(x * 4) + 1];
        var red = (uint)row[(x * 4) + 2];
        if (!hasAlpha)
        {
            return 0xFF000000 | (red << 16) | (green << 8) | blue;
        }

        // Clipboard bitmaps carry straight alpha; PicKit's pixels are premultiplied.
        var alpha = (uint)row[(x * 4) + 3];
        return (alpha << 24)
            | (((red * alpha) + 127) / 255 << 16)
            | (((green * alpha) + 127) / 255 << 8)
            | (((blue * alpha) + 127) / 255);
    }

    private static bool HasAnyAlpha(byte[] dib, int offset, int stride, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            var row = dib.AsSpan(offset + (y * stride), stride);
            for (var x = 0; x < width; x++)
            {
                if (row[(x * 4) + 3] != 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Gives unusual bitmaps, such as paletted ones, a file header so a decoder can read them.</summary>
    private static byte[] WrapAsBmp(byte[] dib)
    {
        var headerSize = (int)Read32(dib, 0);
        var bits = Read16(dib, 14);
        var compression = Read32(dib, 16);
        var coloursUsed = (int)Read32(dib, 32);

        var palette = bits <= 8 ? (coloursUsed > 0 ? coloursUsed : 1 << bits) * 4 : 0;
        var masks = compression == CompressionBitFields && headerSize == BitmapInfoHeaderSize ? 12 : 0;

        var file = new byte[BitmapFileHeaderSize + dib.Length];
        file[0] = (byte)'B';
        file[1] = (byte)'M';
        Write32(file, 2, (uint)file.Length);
        Write32(file, 10, (uint)(BitmapFileHeaderSize + headerSize + masks + palette));
        dib.CopyTo(file, BitmapFileHeaderSize);
        return file;
    }

    private static void CopyRowsBottomUp(ReadOnlySpan<byte> source, int sourceStride, int rowBytes, int height, Span<byte> target)
    {
        for (var y = 0; y < height; y++)
        {
            source.Slice(y * sourceStride, rowBytes).CopyTo(target.Slice((height - 1 - y) * rowBytes, rowBytes));
        }
    }

    private static uint Read32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

    private static ushort Read16(byte[] data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));

    private static void Write32(Span<byte> data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(offset, 4), value);

    private static void Write16(Span<byte> data, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(data.Slice(offset, 2), value);
}
