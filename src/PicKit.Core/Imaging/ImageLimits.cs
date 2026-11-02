namespace PicKit.Core.Imaging;

/// <summary>Guards against images that would exhaust memory, including deliberately crafted ones.</summary>
public static class ImageLimits
{
    public const int BytesPerPixel = 4;

    /// <summary>The largest side any of PicKit's codecs handle.</summary>
    public const int MaxDimension = 65_535;

    /// <summary>The WebP container cannot store a side longer than this.</summary>
    public const int MaxWebPDimension = 16_383;

    private const long AbsoluteMaxPixels = 1_500_000_000;

    /// <summary>Pixel count this machine can realistically hold, leaving room for undo and rendering.</summary>
    public static long MaxPixels
    {
        get
        {
            var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            var affordable = available / 2 / BytesPerPixel;
            return Math.Min(affordable, AbsoluteMaxPixels);
        }
    }

    public static bool Fits(int width, int height) =>
        width > 0
        && height > 0
        && width <= MaxDimension
        && height <= MaxDimension
        && (long)width * height <= MaxPixels;

    public static void EnsureFits(int width, int height)
    {
        if (!Fits(width, height))
        {
            throw new ImageException(
                ImageErrorKind.TooLarge,
                "This image is too large to work with on this PC.",
                $"Requested {width} x {height}; limit is {MaxPixels:N0} pixels and {MaxDimension} per side.");
        }
    }
}
