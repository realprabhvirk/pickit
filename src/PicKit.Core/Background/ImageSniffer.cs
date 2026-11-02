using System.Text;

namespace PicKit.Core.Background;

/// <summary>Identifies an image from its first bytes.</summary>
public static class ImageSniffer
{
    private const int HeaderLength = 12;

    /// <summary>The right extension for this data, or null if it isn't a format PicKit opens.</summary>
    public static string? ExtensionFor(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderLength)
        {
            return null;
        }

        if (data.StartsWith<byte>([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return ".png";
        }

        if (data.StartsWith<byte>([0xFF, 0xD8, 0xFF]))
        {
            return ".jpg";
        }

        if (data.StartsWith("GIF8"u8))
        {
            return ".gif";
        }

        if (data.StartsWith("BM"u8))
        {
            return ".bmp";
        }

        if (data.StartsWith<byte>([0x49, 0x49, 0x2A, 0x00]) || data.StartsWith<byte>([0x4D, 0x4D, 0x00, 0x2A]))
        {
            return ".tif";
        }

        if (data.StartsWith("RIFF"u8) && data[8..].StartsWith("WEBP"u8))
        {
            return ".webp";
        }

        return data[4..].StartsWith("ftyp"u8) ? ContainerExtension(data) : null;
    }

    private static string? ContainerExtension(ReadOnlySpan<byte> data) =>
        Encoding.ASCII.GetString(data.Slice(8, 4)) switch
        {
            "avif" or "avis" => ".avif",
            "heic" or "heix" or "heim" or "heis" or "hevc" or "mif1" => ".heic",
            _ => null,
        };
}
