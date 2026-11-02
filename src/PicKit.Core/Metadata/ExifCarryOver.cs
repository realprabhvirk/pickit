using System.Buffers.Binary;

namespace PicKit.Core.Metadata;

/// <summary>
/// Moves the EXIF block from one JPEG to another, for users who choose to keep photo details on
/// images whose pixels they haven't edited. The pixels written are already upright, so the
/// orientation tag has to be reset or the photo would be turned twice.
/// </summary>
public static class ExifCarryOver
{
    private const byte Marker = 0xFF;
    private const byte App1 = 0xE1;
    private const byte StartOfScan = 0xDA;
    private const ushort OrientationTag = 0x0112;
    private const ushort ShortType = 3;

    private static readonly byte[] ExifHeader = "Exif\0\0"u8.ToArray();

    /// <summary>Returns the APP1 EXIF segment with orientation reset, or null if there isn't one.</summary>
    public static byte[]? ExtractUprightExif(byte[] jpeg)
    {
        try
        {
            var position = 2;
            while (position + 4 <= jpeg.Length && jpeg[position] == Marker)
            {
                var marker = jpeg[position + 1];
                if (marker == StartOfScan)
                {
                    return null;
                }

                var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(position + 2, 2));
                if (marker == App1 && jpeg.AsSpan(position + 4).StartsWith(ExifHeader))
                {
                    var segment = jpeg.AsSpan(position, 2 + length).ToArray();
                    ResetOrientation(segment);
                    return segment;
                }

                position += 2 + length;
            }
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            // A damaged EXIF block isn't worth carrying over.
        }

        return null;
    }

    /// <summary>Inserts a segment straight after the JPEG's start marker.</summary>
    public static byte[] Insert(byte[] jpeg, byte[] segment)
    {
        var result = new byte[jpeg.Length + segment.Length];
        jpeg.AsSpan(0, 2).CopyTo(result);
        segment.CopyTo(result, 2);
        jpeg.AsSpan(2).CopyTo(result.AsSpan(2 + segment.Length));
        return result;
    }

    private static void ResetOrientation(byte[] segment)
    {
        // Segment layout: marker (2), length (2), "Exif\0\0" (6), then a TIFF block.
        const int tiffStart = 10;
        var tiff = segment.AsSpan(tiffStart);
        var littleEndian = tiff[0] == (byte)'I';

        ushort Read16(Span<byte> at) =>
            littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(at) : BinaryPrimitives.ReadUInt16BigEndian(at);

        uint Read32(Span<byte> at) =>
            littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(at) : BinaryPrimitives.ReadUInt32BigEndian(at);

        var directory = (int)Read32(tiff[4..]);
        var entries = Read16(tiff[directory..]);
        for (var i = 0; i < entries; i++)
        {
            var entry = tiff.Slice(directory + 2 + (i * 12), 12);
            if (Read16(entry) != OrientationTag || Read16(entry[2..]) != ShortType)
            {
                continue;
            }

            if (littleEndian)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(entry[8..], 1);
            }
            else
            {
                BinaryPrimitives.WriteUInt16BigEndian(entry[8..], 1);
            }

            return;
        }
    }
}
