using SkiaSharp;

namespace PicKit.Core.Imaging;

/// <summary>Quarter-turn rotations and mirrors. These move whole pixels, so they lose nothing.</summary>
public enum OrthoTransform
{
    Rotate90,
    Rotate180,
    Rotate270,
    FlipHorizontal,
    FlipVertical,
    Transpose,
    Transverse,
}

public static class OrthoTransforms
{
    public static bool SwapsSides(this OrthoTransform transform) =>
        transform is OrthoTransform.Rotate90 or OrthoTransform.Rotate270
            or OrthoTransform.Transpose or OrthoTransform.Transverse;

    public static ImageBuffer Apply(ImageBuffer source, OrthoTransform transform)
    {
        var (width, height) = transform.SwapsSides()
            ? (source.Height, source.Width)
            : (source.Width, source.Height);

        var target = ImageBuffer.Create(width, height);
        Parallel.For(0, height, y =>
        {
            var from = source.Pixels;
            var to = target.Pixels;
            var row = y * width;
            var sourceWidth = source.Width;
            for (var x = 0; x < width; x++)
            {
                var (sourceX, sourceY) = SourcePixel(transform, x, y, width, height);
                to[row + x] = from[(sourceY * sourceWidth) + sourceX];
            }
        });

        return target;
    }

    /// <summary>
    /// Maps continuous image coordinates to where they land after the transform, so anything
    /// positioned on the image can follow its pixels.
    /// </summary>
    public static SKMatrix ToMatrix(OrthoTransform transform, int sourceWidth, int sourceHeight)
    {
        float w = sourceWidth;
        float h = sourceHeight;
        return transform switch
        {
            OrthoTransform.Rotate90 => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            OrthoTransform.Rotate180 => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            OrthoTransform.Rotate270 => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            OrthoTransform.FlipHorizontal => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            OrthoTransform.FlipVertical => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            OrthoTransform.Transpose => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            OrthoTransform.Transverse => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(transform)),
        };
    }

    private static (int X, int Y) SourcePixel(OrthoTransform transform, int x, int y, int width, int height) => transform switch
    {
        OrthoTransform.Rotate90 => (y, width - 1 - x),
        OrthoTransform.Rotate180 => (width - 1 - x, height - 1 - y),
        OrthoTransform.Rotate270 => (height - 1 - y, x),
        OrthoTransform.FlipHorizontal => (width - 1 - x, y),
        OrthoTransform.FlipVertical => (x, height - 1 - y),
        OrthoTransform.Transpose => (y, x),
        OrthoTransform.Transverse => (height - 1 - y, width - 1 - x),
        _ => throw new ArgumentOutOfRangeException(nameof(transform)),
    };
}
