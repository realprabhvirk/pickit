using PicKit.Core.Diagnostics;
using SkiaSharp;

namespace PicKit.Core.Imaging;

/// <summary>A decoded image, already upright and converted to sRGB.</summary>
public sealed class DecodedImage : IDisposable
{
    public DecodedImage(ImageBuffer pixels, ImageFormat format)
    {
        Pixels = pixels;
        Format = format;
    }

    public ImageBuffer Pixels { get; }

    public ImageFormat Format { get; }

    public int FrameCount { get; init; } = 1;

    /// <summary>True when the file only partly decoded, for example because it was cut short.</summary>
    public bool IsIncomplete { get; init; }

    public bool IsAnimated => FrameCount > 1;

    public void Dispose() => Pixels.Dispose();
}

public static class ImageDecoder
{
    private const string CouldNotOpen = "PicKit couldn't open this image.";

    /// <summary>
    /// Second chance for formats Skia lacks. The app wires this to Windows Imaging Component,
    /// which also picks up codecs the user has installed, such as HEIF.
    /// </summary>
    public static Func<byte[], DecodedImage?>? PlatformDecoder { get; set; }

    public static DecodedImage Decode(string path, CancellationToken cancellationToken = default)
    {
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new ImageException(
                ImageErrorKind.Unreadable,
                $"{CouldNotOpen}\n\nThe file may have been moved, or you may not have permission to read it.",
                ex.Message,
                ex);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Decode(data, cancellationToken);
    }

    public static DecodedImage Decode(byte[] data, CancellationToken cancellationToken = default)
    {
        if (data.Length == 0)
        {
            throw new ImageException(ImageErrorKind.Unreadable, $"{CouldNotOpen}\n\nThe file is empty.");
        }

        try
        {
            return DecodeWithSkia(data, cancellationToken) ?? DecodeWithPlatform(data);
        }
        catch (Exception ex) when (ex is not (ImageException or OperationCanceledException))
        {
            // Decoders run over untrusted bytes. Whatever they throw, the app stays up.
            Log.Warn("Image decode failed", ex);
            throw Corrupt(ex.Message, ex);
        }
    }

    private static DecodedImage? DecodeWithSkia(byte[] data, CancellationToken cancellationToken)
    {
        using var source = new SKManagedStream(new MemoryStream(data, writable: false), disposeManagedStream: true);
        using var codec = SKCodec.Create(source, out _);
        if (codec is null)
        {
            return null;
        }

        var width = codec.Info.Width;
        var height = codec.Info.Height;
        ImageLimits.EnsureFits(width, height);

        var pixels = ImageBuffer.Create(width, height);
        try
        {
            // Asking for sRGB output makes the codec apply any embedded colour profile.
            var target = ImageBuffer.InfoFor(width, height).WithColorSpace(SKColorSpace.CreateSrgb());
            var result = codec.GetPixels(target, pixels.Bitmap.GetPixels());

            var incomplete = result is SKCodecResult.IncompleteInput or SKCodecResult.ErrorInInput;
            if (result != SKCodecResult.Success && !incomplete)
            {
                throw Corrupt($"Decoder returned {result}.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            var upright = Upright(pixels, codec.EncodedOrigin);
            if (!ReferenceEquals(upright, pixels))
            {
                pixels.Dispose();
                pixels = upright;
            }

            return new DecodedImage(pixels, ToImageFormat(codec.EncodedFormat))
            {
                FrameCount = Math.Max(1, codec.FrameCount),
                IsIncomplete = incomplete,
            };
        }
        catch
        {
            pixels.Dispose();
            throw;
        }
    }

    private static DecodedImage DecodeWithPlatform(byte[] data)
    {
        return PlatformDecoder?.Invoke(data) ?? throw new ImageException(
            ImageErrorKind.Unsupported,
            $"{CouldNotOpen}\n\nThe file may be corrupted, or the format may not be supported.");
    }

    private static ImageBuffer Upright(ImageBuffer pixels, SKEncodedOrigin origin) => origin switch
    {
        SKEncodedOrigin.TopRight => OrthoTransforms.Apply(pixels, OrthoTransform.FlipHorizontal),
        SKEncodedOrigin.BottomRight => OrthoTransforms.Apply(pixels, OrthoTransform.Rotate180),
        SKEncodedOrigin.BottomLeft => OrthoTransforms.Apply(pixels, OrthoTransform.FlipVertical),
        SKEncodedOrigin.LeftTop => OrthoTransforms.Apply(pixels, OrthoTransform.Transpose),
        SKEncodedOrigin.RightTop => OrthoTransforms.Apply(pixels, OrthoTransform.Rotate90),
        SKEncodedOrigin.RightBottom => OrthoTransforms.Apply(pixels, OrthoTransform.Transverse),
        SKEncodedOrigin.LeftBottom => OrthoTransforms.Apply(pixels, OrthoTransform.Rotate270),
        _ => pixels,
    };

    private static ImageFormat ToImageFormat(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Jpeg => ImageFormat.Jpeg,
        SKEncodedImageFormat.Png => ImageFormat.Png,
        SKEncodedImageFormat.Webp => ImageFormat.WebP,
        SKEncodedImageFormat.Gif => ImageFormat.Gif,
        SKEncodedImageFormat.Bmp => ImageFormat.Bmp,
        SKEncodedImageFormat.Ico => ImageFormat.Ico,
        SKEncodedImageFormat.Heif => ImageFormat.Heif,
        SKEncodedImageFormat.Avif => ImageFormat.Avif,
        _ => ImageFormat.Unknown,
    };

    private static ImageException Corrupt(string details, Exception? inner = null) => new(
        ImageErrorKind.Unreadable,
        $"{CouldNotOpen}\n\nThe file may be corrupted, or the format may not be supported.",
        details,
        inner);
}
