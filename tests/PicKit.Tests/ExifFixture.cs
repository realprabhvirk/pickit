using System.Buffers.Binary;
using System.IO.Hashing;
using System.Text;

namespace PicKit.Tests;

/// <summary>Hand-built metadata, so tests can prove removal against known private values.</summary>
internal static class ExifFixture
{
    public const string SecretMake = "SecretCam";
    public const string SecretComment = "Taken at my home address";

    /// <summary>A TIFF block with a camera make, an orientation, and GPS coordinates.</summary>
    public static byte[] Tiff(ushort orientation = 1)
    {
        var make = Encoding.ASCII.GetBytes(SecretMake + "\0");

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("II"u8);
        writer.Write((ushort)42);
        writer.Write(8u);

        // IFD0: Make, Orientation, pointer to GPS.
        const int ifd0Entries = 3;
        const int ifd0End = 8 + 2 + (ifd0Entries * 12) + 4;
        var makeOffset = ifd0End;
        var gpsOffset = makeOffset + make.Length + (make.Length % 2);

        writer.Write((ushort)ifd0Entries);
        Entry(writer, 0x010F, 2, (uint)make.Length, (uint)makeOffset);
        Entry(writer, 0x0112, 3, 1, orientation);
        Entry(writer, 0x8825, 4, 1, (uint)gpsOffset);
        writer.Write(0u);

        writer.Write(make);
        if (make.Length % 2 == 1)
        {
            writer.Write((byte)0);
        }

        // GPS IFD: 27 degrees south, 153 degrees east.
        const int gpsEntries = 4;
        var rationals = gpsOffset + 2 + (gpsEntries * 12) + 4;
        writer.Write((ushort)gpsEntries);
        Entry(writer, 0x0001, 2, 2, 'S');
        Entry(writer, 0x0002, 5, 3, (uint)rationals);
        Entry(writer, 0x0003, 2, 2, 'E');
        Entry(writer, 0x0004, 5, 3, (uint)(rationals + 24));
        writer.Write(0u);

        foreach (var degrees in new uint[] { 27, 153 })
        {
            writer.Write(degrees);
            writer.Write(1u);
            writer.Write(0u);
            writer.Write(1u);
            writer.Write(0u);
            writer.Write(1u);
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Adds EXIF, XMP and a comment to a JPEG, as a phone or editor would.</summary>
    public static byte[] WithJpegMetadata(byte[] jpeg, ushort orientation = 1)
    {
        var exif = "Exif\0\0"u8.ToArray().Concat(Tiff(orientation)).ToArray();
        var xmp = Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0<x:xmpmeta>" + SecretMake + "</x:xmpmeta>");
        var comment = Encoding.ASCII.GetBytes(SecretComment);

        using var stream = new MemoryStream();
        stream.Write(jpeg, 0, 2);
        JpegSegment(stream, 0xE1, exif);
        JpegSegment(stream, 0xE1, xmp);
        JpegSegment(stream, 0xFE, comment);
        stream.Write(jpeg, 2, jpeg.Length - 2);
        return stream.ToArray();
    }

    /// <summary>Adds text and EXIF chunks to a PNG, straight after the header.</summary>
    public static byte[] WithPngMetadata(byte[] png)
    {
        const int headerEnd = 8 + 25;
        using var stream = new MemoryStream();
        stream.Write(png, 0, headerEnd);
        PngChunk(stream, "tEXt", Encoding.ASCII.GetBytes("Comment\0" + SecretComment));
        PngChunk(stream, "eXIf", Tiff());
        PngChunk(stream, "tIME", [0x07, 0xEA, 11, 2, 9, 30, 0]);
        stream.Write(png, headerEnd, png.Length - headerEnd);
        return stream.ToArray();
    }

    /// <summary>Wraps a simple lossy WebP in the extended format, with EXIF and XMP chunks.</summary>
    public static byte[] WithWebPMetadata(byte[] webp, int width, int height)
    {
        var imageChunks = webp.AsSpan(12).ToArray();

        using var body = new MemoryStream();
        body.Write("WEBP"u8);

        var vp8x = new byte[10];
        vp8x[0] = 0x08 | 0x04;
        WriteUInt24(vp8x.AsSpan(4), width - 1);
        WriteUInt24(vp8x.AsSpan(7), height - 1);
        WebPChunk(body, "VP8X", vp8x);
        body.Write(imageChunks);
        WebPChunk(body, "EXIF", Tiff());
        WebPChunk(body, "XMP ", Encoding.ASCII.GetBytes("<x:xmpmeta>" + SecretMake + "</x:xmpmeta>"));

        var result = new byte[8 + body.Length];
        "RIFF"u8.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)body.Length);
        body.ToArray().CopyTo(result, 8);
        return result;
    }

    public static bool ContainsSecret(byte[] file) =>
        Contains(file, SecretMake) || Contains(file, SecretComment);

    public static bool Contains(byte[] file, string text) =>
        file.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0;

    private static void Entry(BinaryWriter writer, ushort tag, ushort type, uint count, uint value)
    {
        writer.Write(tag);
        writer.Write(type);
        writer.Write(count);
        writer.Write(value);
    }

    private static void JpegSegment(Stream stream, byte marker, byte[] payload)
    {
        stream.WriteByte(0xFF);
        stream.WriteByte(marker);
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)(payload.Length + 2));
        stream.Write(length);
        stream.Write(payload);
    }

    private static void PngChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);

        var typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        stream.Write(typed);

        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32.HashToUInt32(typed));
        stream.Write(crc);
    }

    private static void WebPChunk(Stream stream, string type, byte[] data)
    {
        stream.Write(Encoding.ASCII.GetBytes(type));
        var size = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)data.Length);
        stream.Write(size);
        stream.Write(data);
        if (data.Length % 2 == 1)
        {
            stream.WriteByte(0);
        }
    }

    private static void WriteUInt24(Span<byte> target, int value)
    {
        target[0] = (byte)value;
        target[1] = (byte)(value >> 8);
        target[2] = (byte)(value >> 16);
    }
}
