using SkiaSharp;

namespace PicKit.Core.Imaging;

/// <summary>How an area of the image is chosen. A box is the whole region; the rest are outlines.</summary>
public enum RegionShape
{
    Box,
    Rounded,
    Ellipse,
    Brush,
}

/// <summary>
/// The exact area of an edit that isn't a plain box, in image coordinates. It's either a closed
/// shape, or a brush stroke when <see cref="StrokeWidth"/> is above zero.
/// </summary>
public sealed class RegionOutline : IDisposable
{
    private const float CornerShare = 0.2f;

    private RegionOutline(SKPath path, float strokeWidth)
    {
        Path = path;
        StrokeWidth = strokeWidth;
    }

    public SKPath Path { get; }

    public float StrokeWidth { get; }

    public SKRect Bounds
    {
        get
        {
            var bounds = Path.TightBounds;
            return SKRect.Inflate(bounds, StrokeWidth / 2, StrokeWidth / 2);
        }
    }

    public static RegionOutline ForBox(SKRect box, RegionShape shape)
    {
        using var builder = new SKPathBuilder();
        if (shape == RegionShape.Ellipse)
        {
            builder.AddOval(box);
        }
        else
        {
            var radius = Math.Min(box.Width, box.Height) * CornerShare;
            builder.AddRoundRect(new SKRoundRect(box, radius));
        }

        return new RegionOutline(builder.Detach(), 0);
    }

    public static RegionOutline ForStroke(IReadOnlyList<SKPoint> points, float width)
    {
        using var builder = new SKPathBuilder();
        builder.MoveTo(points[0]);

        // Even a single click paints a dot, which needs some length for its round caps to show.
        builder.LineTo(points.Count == 1 ? new SKPoint(points[0].X + 0.01f, points[0].Y) : points[1]);
        for (var i = 2; i < points.Count; i++)
        {
            builder.LineTo(points[i]);
        }

        return new RegionOutline(builder.Detach(), width);
    }

    public void Dispose() => Path.Dispose();
}
