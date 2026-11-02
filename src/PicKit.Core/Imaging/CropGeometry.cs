using SkiaSharp;

namespace PicKit.Core.Imaging;

public enum CropHandle
{
    None,
    Move,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

/// <summary>Pure crop-box maths, all in image pixels, so every drag is unit-testable.</summary>
public static class CropGeometry
{
    private const float MinimumSide = 1;

    /// <summary>The starting box: the whole image, or the largest centred box of the given shape.</summary>
    public static SKRect Initial(int width, int height, float? aspect)
    {
        if (aspect is not { } ratio)
        {
            return new SKRect(0, 0, width, height);
        }

        var box = ResizeCalculator.CentredCrop(width, height, (int)Math.Round(ratio * 10_000), 10_000);
        return new SKRect(box.Left, box.Top, box.Right, box.Bottom);
    }

    /// <summary>Applies a new aspect ratio, keeping the box centred where it is and as large as fits.</summary>
    public static SKRect WithAspect(SKRect box, float aspect, int width, int height)
    {
        var centreX = box.MidX;
        var centreY = box.MidY;

        var boxWidth = box.Width;
        var boxHeight = boxWidth / aspect;
        if (boxHeight > box.Height)
        {
            boxHeight = box.Height;
            boxWidth = boxHeight * aspect;
        }

        var left = Math.Clamp(centreX - (boxWidth / 2), 0, Math.Max(0, width - boxWidth));
        var top = Math.Clamp(centreY - (boxHeight / 2), 0, Math.Max(0, height - boxHeight));
        return new SKRect(left, top, left + boxWidth, top + boxHeight);
    }

    /// <summary>The box after a drag that began at <paramref name="start"/>.</summary>
    public static SKRect Drag(SKRect start, CropHandle handle, SKPoint delta, float? aspect, int width, int height)
    {
        if (handle == CropHandle.None)
        {
            return start;
        }

        if (handle == CropHandle.Move)
        {
            var left = Math.Clamp(start.Left + delta.X, 0, width - start.Width);
            var top = Math.Clamp(start.Top + delta.Y, 0, height - start.Height);
            return new SKRect(left, top, left + start.Width, top + start.Height);
        }

        return aspect is { } ratio
            ? DragLocked(start, handle, delta, ratio, width, height)
            : DragFree(start, handle, delta, width, height);
    }

    /// <summary>Snaps a box to whole pixels inside the image. Never returns an empty box.</summary>
    public static SKRectI ToPixels(SKRect box, int width, int height)
    {
        var left = Math.Clamp((int)Math.Round(box.Left), 0, width - 1);
        var top = Math.Clamp((int)Math.Round(box.Top), 0, height - 1);
        var right = Math.Clamp((int)Math.Round(box.Right), left + 1, width);
        var bottom = Math.Clamp((int)Math.Round(box.Bottom), top + 1, height);
        return new SKRectI(left, top, right, bottom);
    }

    /// <summary>Finds what a pointer is over. All arguments are in the same coordinate space.</summary>
    public static CropHandle HitTest(SKRect box, SKPoint point, float tolerance)
    {
        var nearLeft = Math.Abs(point.X - box.Left) <= tolerance;
        var nearRight = Math.Abs(point.X - box.Right) <= tolerance;
        var nearTop = Math.Abs(point.Y - box.Top) <= tolerance;
        var nearBottom = Math.Abs(point.Y - box.Bottom) <= tolerance;
        var withinX = point.X >= box.Left - tolerance && point.X <= box.Right + tolerance;
        var withinY = point.Y >= box.Top - tolerance && point.Y <= box.Bottom + tolerance;

        return (nearLeft, nearRight, nearTop, nearBottom) switch
        {
            (true, _, true, _) => CropHandle.TopLeft,
            (_, true, true, _) => CropHandle.TopRight,
            (true, _, _, true) => CropHandle.BottomLeft,
            (_, true, _, true) => CropHandle.BottomRight,
            (true, _, _, _) when withinY => CropHandle.Left,
            (_, true, _, _) when withinY => CropHandle.Right,
            (_, _, true, _) when withinX => CropHandle.Top,
            (_, _, _, true) when withinX => CropHandle.Bottom,
            _ => box.Contains(point) ? CropHandle.Move : CropHandle.None,
        };
    }

    private static SKRect DragFree(SKRect start, CropHandle handle, SKPoint delta, int width, int height)
    {
        var (left, top, right, bottom) = (start.Left, start.Top, start.Right, start.Bottom);

        if (handle is CropHandle.Left or CropHandle.TopLeft or CropHandle.BottomLeft)
        {
            left = Math.Clamp(start.Left + delta.X, 0, right - MinimumSide);
        }

        if (handle is CropHandle.Right or CropHandle.TopRight or CropHandle.BottomRight)
        {
            right = Math.Clamp(start.Right + delta.X, left + MinimumSide, width);
        }

        if (handle is CropHandle.Top or CropHandle.TopLeft or CropHandle.TopRight)
        {
            top = Math.Clamp(start.Top + delta.Y, 0, bottom - MinimumSide);
        }

        if (handle is CropHandle.Bottom or CropHandle.BottomLeft or CropHandle.BottomRight)
        {
            bottom = Math.Clamp(start.Bottom + delta.Y, top + MinimumSide, height);
        }

        return new SKRect(left, top, right, bottom);
    }

    private static SKRect DragLocked(SKRect start, CropHandle handle, SKPoint delta, float aspect, int width, int height)
    {
        // Edge drags grow symmetrically around the opposite edge's centre; corners pivot on the opposite corner.
        var anchor = AnchorFor(start, handle);
        var growsLeft = handle is CropHandle.TopLeft or CropHandle.BottomLeft or CropHandle.Left;
        var growsUp = handle is CropHandle.TopLeft or CropHandle.TopRight or CropHandle.Top;
        var isSideEdge = handle is CropHandle.Left or CropHandle.Right;
        var isTopEdge = handle is CropHandle.Top or CropHandle.Bottom;

        var wanted = WantedSize(start, delta, growsLeft, growsUp, isSideEdge, isTopEdge, aspect);

        // Room available from the anchor to the image's edge, in each direction.
        var roomX = isTopEdge
            ? 2 * Math.Min(anchor.X, width - anchor.X)
            : growsLeft ? anchor.X : width - anchor.X;
        var roomY = isSideEdge
            ? 2 * Math.Min(anchor.Y, height - anchor.Y)
            : growsUp ? anchor.Y : height - anchor.Y;

        var boxWidth = Math.Max(Math.Min(wanted, Math.Min(roomX, roomY * aspect)), Math.Max(MinimumSide, MinimumSide * aspect));
        var boxHeight = boxWidth / aspect;

        var left = isTopEdge ? anchor.X - (boxWidth / 2) : growsLeft ? anchor.X - boxWidth : anchor.X;
        var top = isSideEdge ? anchor.Y - (boxHeight / 2) : growsUp ? anchor.Y - boxHeight : anchor.Y;
        return new SKRect(left, top, left + boxWidth, top + boxHeight);
    }

    private static float WantedSize(
        SKRect start, SKPoint delta, bool growsLeft, bool growsUp, bool isSideEdge, bool isTopEdge, float aspect)
    {
        var fromWidth = start.Width + (growsLeft ? -delta.X : delta.X);
        var fromHeight = (start.Height + (growsUp ? -delta.Y : delta.Y)) * aspect;

        if (isSideEdge)
        {
            return fromWidth;
        }

        // Corners follow whichever axis the pointer has moved further along.
        return isTopEdge ? fromHeight : Math.Max(fromWidth, fromHeight);
    }

    private static SKPoint AnchorFor(SKRect box, CropHandle handle) => handle switch
    {
        CropHandle.TopLeft => new SKPoint(box.Right, box.Bottom),
        CropHandle.TopRight => new SKPoint(box.Left, box.Bottom),
        CropHandle.BottomLeft => new SKPoint(box.Right, box.Top),
        CropHandle.BottomRight => new SKPoint(box.Left, box.Top),
        CropHandle.Left => new SKPoint(box.Right, box.MidY),
        CropHandle.Right => new SKPoint(box.Left, box.MidY),
        CropHandle.Top => new SKPoint(box.MidX, box.Bottom),
        CropHandle.Bottom => new SKPoint(box.MidX, box.Top),
        _ => new SKPoint(box.MidX, box.MidY),
    };
}
