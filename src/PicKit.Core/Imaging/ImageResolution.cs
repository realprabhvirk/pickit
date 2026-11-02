using System.Buffers.Binary;
using System.Text;
using PicKit.Core.Metadata;

namespace PicKit.Core.Imaging;

/// <summary>Records how large an image should print. JPEG and PNG hold this; WebP has no field for it.</summary>
public static class ImageResolution
{
    private const int JpegStart = 2;
    private const int JpegUnitsOffset = 13;
    private const byte DotsPerInch = 1;
    private const int PngHeaderEnd = 33;
    private const double MetresPerInch = 0.0254;

    public static bool CanStore(ImageFormat format) => format is ImageFormat.Jpeg or ImageFormat.Png;

    /// <summary>Returns the file with its print resolution set, leaving the picture itself as it is.</summary>
    public static byte[] Stamp(byte[] data, ImageFormat format, int dpi) => format switch
    {
        ImageFormat.Jpeg => StampJpeg(data, dpi),
        ImageFormat.Png => StampPng(data, dpi),
        _ => data,
    };

    private static byte[] StampJpeg(byte[] data, int dpi)
    {
        var density = (ushort)Math.Clamp(dpi, 1, ushort.MaxValue);
        if (StartsWithJfifHeader(data))
        {
            var stamped = (byte[])data.Clone();
            WriteDensity(stamped.AsSpan(JpegUnitsOffset), density);
            return stamped;
        }

        // FF E0, length 16, "JFIF", version 1.1, density, no thumbnail.
        Span<byte> header = [0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 0, 0, 0, 0, 0];
        WriteDensity(header[(JpegUnitsOffset - JpegStart)..], density);

        var result = new byte[data.Length + header.Length];
        data.AsSpan(0, JpegStart).CopyTo(result);
        header.CopyTo(result.AsSpan(JpegStart));
        data.AsSpan(JpegStart).CopyTo(result.AsSpan(JpegStart + header.Length));
        return result;
    }

    private static bool StartsWithJfifHeader(byte[] data) =>
        data.Length > JpegUnitsOffset + 5
        && data[JpegStart] == 0xFF
        && data[JpegStart + 1] == 0xE0
        && data.AsSpan(JpegStart + 4, 5).SequenceEqual("JFIF\0"u8);

    private static void WriteDensity(Span<byte> target, ushort density)
    {
        target[0] = DotsPerInch;
        BinaryPrimitives.WriteUInt16BigEndian(target[1..], density);
        BinaryPrimitives.WriteUInt16BigEndian(target[3..], density);
    }

    private static byte[] StampPng(byte[] data, int dpi)
    {
        const int dataLength = 9;
        var pixelsPerMetre = (uint)Math.Round(dpi / MetresPerInch);

        var chunk = new byte[4 + 4 + dataLength + 4];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, dataLength);
        Encoding.ASCII.GetBytes("pHYs", chunk.AsSpan(4));
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8), pixelsPerMetre);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(12), pixelsPerMetre);
        chunk[16] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(17), MetadataStripper.Crc32(chunk.AsSpan(4, 4 + dataLength)));

        var result = new byte[data.Length + chunk.Length];
        data.AsSpan(0, PngHeaderEnd).CopyTo(result);
        chunk.CopyTo(result.AsSpan(PngHeaderEnd));
        data.AsSpan(PngHeaderEnd).CopyTo(result.AsSpan(PngHeaderEnd + chunk.Length));
        return result;
    }
}
