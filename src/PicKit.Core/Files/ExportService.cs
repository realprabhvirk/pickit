using PicKit.Core.Imaging;
using PicKit.Core.Metadata;

namespace PicKit.Core.Files;

/// <summary>A finished export.</summary>
public sealed record ExportResult(string Path, long Size);

public static class ExportService
{
    /// <summary>Encodes and writes an image. The target is replaced only once the file is complete.</summary>
    public static ExportResult Save(ImageBuffer image, EncodeOptions options, string path, byte[]? exifSegment = null)
    {
        var data = ImageEncoder.Encode(image, options);
        if (exifSegment is not null && options.Format == ImageFormat.Jpeg)
        {
            data = ExifCarryOver.Insert(data, exifSegment);
        }

        return WriteBytes(data, path);
    }

    public static ExportResult WriteBytes(byte[] data, string path)
    {
        try
        {
            SafeFile.Write(path, stream => stream.Write(data));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ImageException(
                ImageErrorKind.EncodeFailed,
                $"PicKit couldn't save to this location.\n\nThe folder may be read-only, the disk may be full, or the file may be open in another app.",
                ex.Message,
                ex);
        }

        return new ExportResult(path, data.LongLength);
    }

    /// <summary>Writes a metadata-free copy, losslessly where the format allows.</summary>
    public static ExportResult WriteClean(string sourcePath, string targetPath)
    {
        var original = File.ReadAllBytes(sourcePath);
        var stripped = MetadataStripper.Strip(original);

        using var decoded = ImageDecoder.Decode(original);

        // Stripping removes the orientation tag too. If the photo relied on it, the upright
        // pixels have to be re-encoded or it would open sideways.
        var usable = stripped is not null && LooksUpright(decoded, stripped);
        if (usable)
        {
            return WriteBytes(stripped!, targetPath);
        }

        var format = ImageFormats.CanEncode(decoded.Format) ? decoded.Format : ImageFormat.Png;
        const int nearLosslessQuality = 95;
        return Save(decoded.Pixels, new EncodeOptions(format, nearLosslessQuality), targetPath);
    }

    /// <summary>Output name for an operation, never overwriting an existing file.</summary>
    public static string OutputPathFor(string sourcePath, string suffix, ImageFormat format)
    {
        var extension = format == ImageFormats.FromExtension(sourcePath)
            ? Path.GetExtension(sourcePath)
            : ImageFormats.Extension(format);

        return FileNames.MakeUnique(FileNames.WithSuffix(sourcePath, suffix, extension));
    }

    private static bool LooksUpright(DecodedImage decoded, byte[] stripped)
    {
        using var check = ImageDecoder.Decode(stripped);
        return check.Pixels.Width == decoded.Pixels.Width
            && check.Pixels.Height == decoded.Pixels.Height
            && check.Pixels.Pixels.SequenceEqual(decoded.Pixels.Pixels);
    }
}
