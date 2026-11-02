using SkiaSharp;

namespace PicKit.Core.Imaging;

/// <summary>Low-level loops over premultiplied BGRA pixels.</summary>
public static class PixelOps
{
    private const uint EvenChannels = 0x00FF00FF;
    private const uint RoundingForFour = 0x00020002;

    /// <summary>Averages each 2 x 2 block into one pixel, for clean repeated downscaling.</summary>
    public static ImageBuffer Halve(ImageBuffer source)
    {
        var target = ImageBuffer.Create(Math.Max(1, source.Width / 2), Math.Max(1, source.Height / 2));
        HalveRegion(source, target, target.Bounds);
        return target;
    }

    /// <summary>Recomputes part of a half-size image after its source changed.</summary>
    public static void HalveRegion(ImageBuffer source, ImageBuffer target, SKRectI targetRegion)
    {
        var area = SKRectI.Intersect(targetRegion, target.Bounds);
        if (area.IsEmpty)
        {
            return;
        }

        Parallel.For(area.Top, area.Bottom, y =>
        {
            var from = source.Pixels;
            var to = target.Pixels;
            var sourceWidth = source.Width;
            var top = Math.Min(y * 2, source.Height - 1) * sourceWidth;
            var bottom = Math.Min((y * 2) + 1, source.Height - 1) * sourceWidth;
            var row = y * target.Width;

            for (var x = area.Left; x < area.Right; x++)
            {
                var left = Math.Min(x * 2, sourceWidth - 1);
                var right = Math.Min((x * 2) + 1, sourceWidth - 1);
                to[row + x] = Average(from[top + left], from[top + right], from[bottom + left], from[bottom + right]);
            }
        });
    }

    /// <summary>Exchanges a rectangle of <paramref name="image"/> with the whole of <paramref name="patch"/>.</summary>
    public static void SwapRegion(ImageBuffer image, ImageBuffer patch, SKPointI origin)
    {
        for (var y = 0; y < patch.Height; y++)
        {
            var inImage = image.Pixels.Slice(((origin.Y + y) * image.Width) + origin.X, patch.Width);
            var inPatch = patch.Pixels.Slice(y * patch.Width, patch.Width);
            for (var x = 0; x < inImage.Length; x++)
            {
                (inImage[x], inPatch[x]) = (inPatch[x], inImage[x]);
            }
        }
    }

    public static ImageBuffer CopyRegion(ImageBuffer source, SKRectI region)
    {
        var target = ImageBuffer.Create(region.Width, region.Height);
        for (var y = 0; y < region.Height; y++)
        {
            source.Pixels
                .Slice(((region.Top + y) * source.Width) + region.Left, region.Width)
                .CopyTo(target.Pixels.Slice(y * region.Width, region.Width));
        }

        return target;
    }

    private static uint Average(uint a, uint b, uint c, uint d)
    {
        var even = (a & EvenChannels) + (b & EvenChannels) + (c & EvenChannels) + (d & EvenChannels) + RoundingForFour;
        var odd = ((a >> 8) & EvenChannels) + ((b >> 8) & EvenChannels)
            + ((c >> 8) & EvenChannels) + ((d >> 8) & EvenChannels) + RoundingForFour;

        return ((even >> 2) & EvenChannels) | (((odd >> 2) & EvenChannels) << 8);
    }
}
