using System.Buffers.Binary;
using System.Text;

namespace PicKit.Core.Metadata;

/// <summary>
/// Removes metadata by rewriting the file's container, without touching the compressed image,
/// so a JPEG loses its GPS tag but not a single pixel of quality. Everything is keep-listed:
/// only structures an image needs survive, so unknown or vendor data can't slip through.
/// </summary>
public static class MetadataStripper
{
    private const byte Marker = 0xFF;
    private const byte StartOfImage = 0xD8;
    private const byte EndOfImage = 0xD9;
    private const byte StartOfScan = 0xDA;
    private const byte AppFirst = 0xE0;
    private const byte AppIcc = 0xE2;
    private const byte AppAdobe = 0xEE;
    private const byte AppLast = 0xEF;
    private const byte Comment = 0xFE;

    private const byte WebPExifFlag = 0x08;
    private const byte WebPXmpFlag = 0x04;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly HashSet<string> PngKeptChunks =
        ["IHDR", "PLTE", "IDAT", "IEND", "tRNS", "gAMA", "cHRM", "sRGB", "iCCP", "sBIT", "acTL", "fcTL", "fdAT"];

    private static readonly HashSet<string> WebPKeptChunks =
        ["VP8 ", "VP8L", "VP8X", "ALPH", "ANIM", "ANMF", "ICCP"];

    /// <summary>
    /// Returns the file without metadata, or null if the format isn't handled here or the file
    /// isn't well-formed enough to rewrite safely. Callers then fall back to re-encoding pixels.
    /// </summary>
    public static byte[]? Strip(byte[] file)
    {
        try
        {
            if (file.Length > 2 && file[0] == Marker && file[1] == StartOfImage)
            {
                return StripJpeg(file);
            }

            if (file.AsSpan().StartsWith(PngSignature))
            {
                return StripPng(file);
            }

            if (IsWebP(file))
            {
                return StripWebP(file);
            }
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException)
        {
            // Malformed input. Nothing is returned, so nothing half-cleaned can reach the user.
        }

        return null;
    }

    private static byte[] StripJpeg(byte[] file)
    {
        using var output = new MemoryStream(file.Length);
        output.Write(file, 0, 2);

        var position = 2;
        while (true)
        {
            if (file[position] != Marker)
            {
                throw new InvalidDataException("Expected a JPEG marker.");
            }

            var marker = file[position + 1];
            if (marker == Marker)
            {
                position++;
                continue;
            }

            if (marker == StartOfScan)
            {
                // Images are often followed by extra data, such as a second picture on phones.
                var end = EndOfJpegScan(file, position);
                output.Write(file, position, end - position);
                return output.ToArray();
            }

            if (marker == EndOfImage)
            {
                output.Write(file, position, 2);
                return output.ToArray();
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(position + 2, 2));
            if (length < 2 || position + 2 + length > file.Length)
            {
                throw new InvalidDataException("A JPEG segment runs past the end of the file.");
            }

            if (ShouldKeepJpegSegment(file, position, marker, length))
            {
                output.Write(file, position, 2 + length);
            }

            position += 2 + length;
        }
    }

    private static bool ShouldKeepJpegSegment(byte[] file, int position, byte marker, int length)
    {
        if (marker == Comment)
        {
            return false;
        }

        if (marker is < AppFirst or > AppLast)
        {
            return true;
        }

        var payload = file.AsSpan(position + 4, length - 2);
        return marker switch
        {
            AppFirst => payload.StartsWith("JFIF\0"u8),
            AppIcc => payload.StartsWith("ICC_PROFILE\0"u8),
            AppAdobe => payload.StartsWith("Adobe"u8),
            _ => false,
        };
    }

    /// <summary>Walks entropy-coded data to the real end of the image.</summary>
    private static int EndOfJpegScan(byte[] file, int scanStart)
    {
        var position = scanStart;
        while (position + 1 < file.Length)
        {
            if (file[position] == Marker && file[position + 1] == EndOfImage)
            {
                return position + 2;
            }

            position++;
        }

        throw new InvalidDataException("The JPEG has no end marker.");
    }

    private static byte[] StripPng(byte[] file)
    {
        using var output = new MemoryStream(file.Length);
        output.Write(PngSignature);

        var position = PngSignature.Length;
        while (position + 12 <= file.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(position, 4));
            if (length > int.MaxValue || position + 12 + (long)length > file.Length)
            {
                throw new InvalidDataException("A PNG chunk runs past the end of the file.");
            }

            var type = Encoding.ASCII.GetString(file, position + 4, 4);
            var total = 12 + (int)length;
            if (PngKeptChunks.Contains(type))
            {
                output.Write(file, position, total);
            }

            position += total;
            if (type == "IEND")
            {
                return output.ToArray();
            }
        }

        throw new InvalidDataException("The PNG has no end chunk.");
    }

    private static bool IsWebP(byte[] file) =>
        file.Length >= 12 && file.AsSpan(0, 4).SequenceEqual("RIFF"u8) && file.AsSpan(8, 4).SequenceEqual("WEBP"u8);

    private static byte[] StripWebP(byte[] file)
    {
        using var body = new MemoryStream(file.Length);
        body.Write("WEBP"u8);

        var declared = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(4, 4));
        var end = Math.Min(file.Length, 8 + (long)declared);
        var position = 12;
        while (position + 8 <= end)
        {
            var type = Encoding.ASCII.GetString(file, position, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(position + 4, 4));
            var padded = size + (size % 2);
            if (position + 8 + padded > end)
            {
                throw new InvalidDataException("A WebP chunk runs past the end of the file.");
            }

            if (WebPKeptChunks.Contains(type))
            {
                var start = (int)body.Length;
                body.Write(file, position, 8 + (int)padded);
                if (type == "VP8X")
                {
                    ClearWebPMetadataFlags(body, start);
                }
            }

            position += 8 + (int)padded;
        }

        var bytes = new byte[8 + body.Length];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), (uint)body.Length);
        body.Position = 0;
        body.ReadExactly(bytes, 8, (int)body.Length);
        return bytes;
    }

    private static void ClearWebPMetadataFlags(MemoryStream body, int chunkStart)
    {
        var flagsOffset = chunkStart + 8;
        var buffer = body.GetBuffer();
        buffer[flagsOffset] &= unchecked((byte)~(WebPExifFlag | WebPXmpFlag));
    }
}
