using PicKit.Core.Diagnostics;
using SkiaSharp;

namespace PicKit.Core.Imaging;

public enum CompressionPreset
{
    High,
    Balanced,
    Small,
}

/// <summary>The real outcome of an encode, reported only after it has happened.</summary>
public sealed record CompressionResult(byte[] Data, int Width, int Height, int Quality, bool MetTarget)
{
    public long Size => Data.LongLength;
}

public static class Compression
{
    private const int LowestQuality = 30;
    private const int HighestQuality = 95;
    private const int LosslessQuality = 100;
    private const int MaxResizeRounds = 8;
    private const int SmallestSide = 16;

    /// <summary>A size within this share of the target is close enough to stop searching.</summary>
    private const double CloseEnough = 0.92;

    public static int QualityFor(ImageFormat format, CompressionPreset preset) => (format, preset) switch
    {
        (ImageFormat.WebP, CompressionPreset.High) => 88,
        (ImageFormat.WebP, CompressionPreset.Balanced) => 78,
        (ImageFormat.WebP, CompressionPreset.Small) => 60,
        (_, CompressionPreset.High) => 90,
        (_, CompressionPreset.Balanced) => 80,
        _ => 65,
    };

    /// <summary>
    /// Finds the best quality that fits under <paramref name="targetBytes"/>, shrinking the
    /// dimensions only if quality alone can't get there. PNG is lossless, so it can only shrink.
    /// </summary>
    public static CompressionResult ToTargetSize(
        ImageBuffer image,
        ImageFormat format,
        long targetBytes,
        SKColor? background = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetBytes);

        var current = image;
        CompressionResult? best = null;
        try
        {
            for (var round = 0; round < MaxResizeRounds; round++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report((double)round / MaxResizeRounds);

                var attempt = format == ImageFormat.Png
                    ? AtQuality(current, format, LosslessQuality, background, targetBytes)
                    : SearchQuality(current, format, targetBytes, background, cancellationToken);

                if (attempt.MetTarget)
                {
                    return attempt;
                }

                // Keep the smallest attempt in case nothing ever fits.
                if (best is null || attempt.Size < best.Size)
                {
                    best = attempt;
                }

                if (NextSize(current, attempt.Size, targetBytes) is not { } next)
                {
                    break;
                }

                var resized = ImageOps.Resize(current, next.Width, next.Height);
                if (!ReferenceEquals(current, image))
                {
                    current.Dispose();
                }

                current = resized;
            }

            Log.Info($"Target size not reached: wanted {targetBytes}, got {best?.Size}");
            return best!;
        }
        finally
        {
            if (!ReferenceEquals(current, image))
            {
                current.Dispose();
            }
        }
    }

    private static CompressionResult SearchQuality(
        ImageBuffer image, ImageFormat format, long targetBytes, SKColor? background, CancellationToken cancellationToken)
    {
        var low = LowestQuality;
        var high = HighestQuality;
        CompressionResult? fitting = null;
        CompressionResult? smallest = null;

        while (low <= high)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var quality = (low + high) / 2;
            var attempt = AtQuality(image, format, quality, background, targetBytes);

            if (smallest is null || attempt.Size < smallest.Size)
            {
                smallest = attempt;
            }

            if (!attempt.MetTarget)
            {
                high = quality - 1;
                continue;
            }

            fitting = attempt;
            if (attempt.Size >= targetBytes * CloseEnough)
            {
                break;
            }

            low = quality + 1;
        }

        return fitting ?? smallest!;
    }

    private static CompressionResult AtQuality(
        ImageBuffer image, ImageFormat format, int quality, SKColor? background, long targetBytes)
    {
        var data = ImageEncoder.Encode(image, new EncodeOptions(format, quality, background));
        return new CompressionResult(data, image.Width, image.Height, quality, data.LongLength <= targetBytes);
    }

    private static (int Width, int Height)? NextSize(ImageBuffer image, long actualBytes, long targetBytes)
    {
        // File size follows pixel count, so sides scale with the square root. Aim a little low.
        const double safetyMargin = 0.95;
        var scale = Math.Min(Math.Sqrt((double)targetBytes / actualBytes) * safetyMargin, safetyMargin);

        var width = (int)Math.Round(image.Width * scale);
        var height = (int)Math.Round(image.Height * scale);
        return Math.Min(width, height) < SmallestSide ? null : (width, height);
    }
}
