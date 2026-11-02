using SkiaSharp;

namespace PicKit.Core.Rendering;

/// <summary>
/// The single source of truth for mapping between viewport device pixels and image pixels.
/// A zoom of 1 means one image pixel covers exactly one device pixel. Zooming and panning
/// only ever change this mapping, never the image.
/// </summary>
public sealed class ViewTransform
{
    public const double MinZoom = 0.01;
    public const double MaxZoom = 32;

    /// <summary>Gap kept around a fitted image so handles at its edge stay reachable.</summary>
    public double Padding { get; set; } = 24;

    /// <summary>Device pixels per layout unit, so overlays stay the same size at every DPI.</summary>
    public float DeviceScale { get; set; } = 1;

    public double Zoom { get; private set; } = 1;

    public double OffsetX { get; private set; }

    public double OffsetY { get; private set; }

    public int ImageWidth { get; private set; }

    public int ImageHeight { get; private set; }

    public double ViewportWidth { get; private set; }

    public double ViewportHeight { get; private set; }

    /// <summary>While true, viewport changes re-fit the image rather than holding the zoom.</summary>
    public bool IsFitted { get; private set; } = true;

    /// <summary>Image-to-viewport matrix, with offsets on whole pixels so 100% stays crisp.</summary>
    public SKMatrix Matrix => new(
        (float)Zoom, 0, (float)Math.Round(OffsetX),
        0, (float)Zoom, (float)Math.Round(OffsetY),
        0, 0, 1);

    public void SetImage(int width, int height, bool refit)
    {
        ImageWidth = width;
        ImageHeight = height;
        if (refit)
        {
            Fit();
        }
        else
        {
            Clamp();
        }
    }

    public void SetViewport(double width, double height)
    {
        ViewportWidth = width;
        ViewportHeight = height;
        if (IsFitted)
        {
            Fit();
        }
        else
        {
            Clamp();
        }
    }

    public SKPoint ImageToView(SKPoint point) => new(
        (float)((point.X * Zoom) + Math.Round(OffsetX)),
        (float)((point.Y * Zoom) + Math.Round(OffsetY)));

    public SKPoint ViewToImage(SKPoint point) => new(
        (float)((point.X - Math.Round(OffsetX)) / Zoom),
        (float)((point.Y - Math.Round(OffsetY)) / Zoom));

    public SKRect ImageToView(SKRect rect)
    {
        var topLeft = ImageToView(new SKPoint(rect.Left, rect.Top));
        var bottomRight = ImageToView(new SKPoint(rect.Right, rect.Bottom));
        return new SKRect(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
    }

    /// <summary>Shows the whole image, without enlarging one smaller than the viewport.</summary>
    public void Fit()
    {
        IsFitted = true;
        if (ImageWidth <= 0 || ImageHeight <= 0 || ViewportWidth <= 0 || ViewportHeight <= 0)
        {
            return;
        }

        var available = Math.Min(
            (ViewportWidth - (2 * Padding)) / ImageWidth,
            (ViewportHeight - (2 * Padding)) / ImageHeight);

        Zoom = Math.Clamp(Math.Min(available, 1), MinZoom, MaxZoom);
        Clamp();
    }

    public void ActualSize() => ZoomAt(new SKPoint((float)(ViewportWidth / 2), (float)(ViewportHeight / 2)), 1);

    /// <summary>Zooms while keeping the image point under <paramref name="anchor"/> stationary.</summary>
    public void ZoomAt(SKPoint anchor, double zoom)
    {
        IsFitted = false;
        var imageX = (anchor.X - OffsetX) / Zoom;
        var imageY = (anchor.Y - OffsetY) / Zoom;

        Zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        OffsetX = anchor.X - (imageX * Zoom);
        OffsetY = anchor.Y - (imageY * Zoom);
        Clamp();
    }

    public void PanBy(double deltaX, double deltaY)
    {
        IsFitted = false;
        OffsetX += deltaX;
        OffsetY += deltaY;
        Clamp();
    }

    /// <summary>Centres an axis that fits the viewport; otherwise keeps the image over the viewport.</summary>
    private void Clamp()
    {
        OffsetX = ClampAxis(OffsetX, ImageWidth * Zoom, ViewportWidth);
        OffsetY = ClampAxis(OffsetY, ImageHeight * Zoom, ViewportHeight);
    }

    private double ClampAxis(double offset, double imageSize, double viewportSize)
    {
        if (imageSize + (2 * Padding) <= viewportSize)
        {
            return (viewportSize - imageSize) / 2;
        }

        var lowest = viewportSize - imageSize - Padding;
        return Math.Clamp(offset, Math.Min(lowest, Padding), Padding);
    }
}
