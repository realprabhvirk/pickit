using System.Buffers.Binary;
using System.IO;
using PicKit.Core.Imaging;

namespace PicKit.UiTests;

/// <summary>A small JPEG carrying a GPS position, as a phone would write it.</summary>
internal static class ExifJpeg
{
    public static byte[] Create()
    {
        using var image = ImageBuffer.Create(64, 64);
        image.Bitmap.Erase(SkiaSharp.SKColors.SteelBlue);
        var jpeg = ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Jpeg));

        var tiff = Tiff();
        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();

        using var stream = new MemoryStream();
        stream.Write(jpeg, 0, 2);
        stream.WriteByte(0xFF);
        stream.WriteByte(0xE1);
        var length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)(payload.Length + 2));
        stream.Write(length);
        stream.Write(payload);
        stream.Write(jpeg, 2, jpeg.Length - 2);
        return stream.ToArray();
    }

    private static byte[] Tiff()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("II"u8);
        writer.Write((ushort)42);
        writer.Write(8u);

        const int gpsOffset = 8 + 2 + 12 + 4;
        writer.Write((ushort)1);
        Entry(writer, 0x8825, 4, 1, gpsOffset);
        writer.Write(0u);

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

    private static void Entry(BinaryWriter writer, ushort tag, ushort type, uint count, uint value)
    {
        writer.Write(tag);
        writer.Write(type);
        writer.Write(count);
        writer.Write(value);
    }
}
