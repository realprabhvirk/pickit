using SkiaSharp;

namespace PicKit.Core.Imaging;

/// <summary>How to write an image.</summary>
/// <param name="Format">Must be one <see cref="ImageFormats.CanEncode"/> accepts.</param>
/// <param name="Quality">1 to 100. Ignored by PNG, which is always lossless.</param>
/// <param name="Background">Placed behind transparency when the format has none.</param>
public sealed record EncodeOptions(ImageFormat Format, int Quality = 90, SKColor? Background = null);

public static class ImageEncoder
{
    private const int FullColourFromQuality = 90;
    private const int PngCompressionLevel = 6;

    public static byte[] Encode(ImageBuffer image, EncodeOptions options)
    {
        if (!ImageFormats.CanEncode(options.Format))
        {
            throw new ImageException(
                ImageErrorKind.Unsupported,
                $"PicKit can't write {ImageFormats.DisplayName(options.Format)} files. Try PNG, JPEG or WebP.");
        }

        if (options.Format == ImageFormat.WebP
            && Math.Max(image.Width, image.Height) > ImageLimits.MaxWebPDimension)
        {
            throw new ImageException(
                ImageErrorKind.EncodeFailed,
                $"WebP images can't be larger than {ImageLimits.MaxWebPDimension} pixels on a side. Resize the image or save as PNG or JPEG.");
        }

        if (options.Format == ImageFormat.Jpeg && image.HasTransparency())
        {
            if (options.Background is not { } background)
            {
                throw new ArgumentException("JPEG has no transparency, so a background colour is required.", nameof(options));
            }

            using var flattened = ImageOps.Flatten(image, background);
            return EncodeUnchecked(flattened, options);
        }

        return EncodeUnchecked(image, options);
    }

    public static string MimeTypeFor(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => "image/jpeg",
        ImageFormat.Png => "image/png",
        ImageFormat.WebP => "image/webp",
        _ => "application/octet-stream",
    };

    private static byte[] EncodeUnchecked(ImageBuffer image, EncodeOptions options)
    {
        var quality = Math.Clamp(options.Quality, 1, 100);
        using var pixels = image.Bitmap.PeekPixels();
        using var data = options.Format switch
        {
            ImageFormat.Jpeg => pixels.Encode(JpegOptions(quality)),
            ImageFormat.WebP => pixels.Encode(new SKWebpEncoderOptions(SKWebpEncoderCompression.Lossy, quality)),
            _ => pixels.Encode(new SKPngEncoderOptions(SKPngEncoderFilterFlags.AllFilters, PngCompressionLevel)),
        };

        return data?.ToArray() ?? throw new ImageException(
            ImageErrorKind.EncodeFailed,
            $"PicKit couldn't create the {ImageFormats.DisplayName(options.Format)} file.");
    }

    private static SKJpegEncoderOptions JpegOptions(int quality)
    {
        // Full colour resolution keeps red text and fine detail clean at high quality.
        var subsampling = quality >= FullColourFromQuality
            ? SKJpegEncoderDownsample.Downsample444
            : SKJpegEncoderDownsample.Downsample420;

        return new SKJpegEncoderOptions(quality, subsampling, SKJpegEncoderAlphaOption.Ignore);
    }
}
