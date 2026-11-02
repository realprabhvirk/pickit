using SkiaSharp;

namespace PicKit.Core.Imaging;

/// <summary>
/// Owns one image's pixels. All PicKit pixels are premultiplied BGRA in sRGB, which matches
/// both Skia's native order and WPF's Pbgra32, so nothing is converted while rendering.
/// </summary>
public sealed class ImageBuffer : IDisposable
{
    private ImageBuffer(SKBitmap bitmap)
    {
        Bitmap = bitmap;
    }

    public SKBitmap Bitmap { get; }

    public int Width => Bitmap.Width;

    public int Height => Bitmap.Height;

    public long ByteCount => (long)Bitmap.RowBytes * Bitmap.Height;

    public SKRectI Bounds => new(0, 0, Width, Height);

    /// <summary>All pixels in premultiplied BGRA, row after row with no padding.</summary>
    public unsafe Span<uint> Pixels => new((void*)Bitmap.GetPixels(), checked(Width * Height));

    public static SKImageInfo InfoFor(int width, int height) =>
        new(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

    /// <summary>Allocates a fully transparent image.</summary>
    public static ImageBuffer Create(int width, int height)
    {
        ImageLimits.EnsureFits(width, height);

        var bitmap = new SKBitmap();
        if (!bitmap.TryAllocPixels(InfoFor(width, height)))
        {
            bitmap.Dispose();
            throw new ImageException(
                ImageErrorKind.TooLarge,
                "There isn't enough memory available to work with this image.",
                $"Failed to allocate {width} x {height}.");
        }

        bitmap.Erase(SKColors.Transparent);
        return new ImageBuffer(bitmap);
    }

    public ImageBuffer Clone()
    {
        var copy = Create(Width, Height);
        Pixels.CopyTo(copy.Pixels);
        return copy;
    }

    public Span<uint> Row(int y) => Pixels.Slice(y * Width, Width);

    public bool HasTransparency()
    {
        const uint opaqueAlpha = 0xFF000000;
        foreach (var pixel in Pixels)
        {
            if ((pixel & opaqueAlpha) != opaqueAlpha)
            {
                return true;
            }
        }

        return false;
    }

    public SKCanvas CreateCanvas() => new(Bitmap);

    /// <summary>
    /// Wraps the pixels as an SKImage without copying. The image is only valid while this
    /// buffer is alive and unmodified.
    /// </summary>
    public SKImage AsImage() => SKImage.FromPixels(Bitmap.PeekPixels());

    public void Dispose() => Bitmap.Dispose();
}
