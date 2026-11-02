using System.Collections.Immutable;
using PicKit.Core.Imaging;
using PicKit.Core.Markup;
using SkiaSharp;

namespace PicKit.Core.Documents;

/// <summary>
/// A blur, pixelate or redaction, worked out but not yet applied, so the heavy part can run off
/// the UI thread. Markup that overlaps a region is burned into the pixels first; otherwise it
/// would stay crisp, and readable, on top of the area the user meant to hide.
/// </summary>
public sealed class RegionEdit : IDisposable
{
    private RegionEdit(ImageBuffer pixels, SKRectI bounds, ImmutableArray<MarkupElement> burnedIn)
    {
        Pixels = pixels;
        Bounds = bounds;
        BurnedIn = burnedIn;
    }

    /// <summary>A patch for <see cref="Bounds"/>, or a full image if markup had to be burned in.</summary>
    internal ImageBuffer Pixels { get; private set; }

    internal SKRectI Bounds { get; }

    internal ImmutableArray<MarkupElement> BurnedIn { get; }

    internal bool ReplacesWholeImage => !BurnedIn.IsEmpty;

    /// <summary>Builds the edit. <paramref name="replacement"/> returns new pixels for one region.</summary>
    public static RegionEdit Prepare(
        ImageBuffer pixels,
        ImmutableArray<MarkupElement> markups,
        IReadOnlyList<SKRectI> regions,
        Func<ImageBuffer, SKRectI, ImageBuffer> replacement)
    {
        var burned = markups.Where(element => regions.Any(region => Overlaps(element.Bounds, region))).ToImmutableArray();

        if (burned.IsEmpty)
        {
            var bounds = Union(regions);
            var patch = PixelOps.CopyRegion(pixels, bounds);
            WriteRegions(pixels, patch, new SKPointI(bounds.Left, bounds.Top), regions, replacement);
            return new RegionEdit(patch, bounds, burned);
        }

        var burnedPixels = pixels.Clone();
        using (var canvas = burnedPixels.CreateCanvas())
        {
            MarkupRenderer.Draw(canvas, burned);
        }

        WriteRegions(burnedPixels, burnedPixels, new SKPointI(0, 0), regions, replacement);
        return new RegionEdit(burnedPixels, burnedPixels.Bounds, burned);
    }

    public void Dispose() => Pixels?.Dispose();

    /// <summary>Hands the pixels to the document, which then owns them.</summary>
    internal ImageBuffer TakePixels()
    {
        var pixels = Pixels;
        Pixels = null!;
        return pixels;
    }

    private static void WriteRegions(
        ImageBuffer source,
        ImageBuffer target,
        SKPointI targetOrigin,
        IReadOnlyList<SKRectI> regions,
        Func<ImageBuffer, SKRectI, ImageBuffer> replacement)
    {
        foreach (var region in regions)
        {
            using var replaced = replacement(source, region);
            for (var y = 0; y < region.Height; y++)
            {
                var row = ((region.Top - targetOrigin.Y + y) * target.Width) + region.Left - targetOrigin.X;
                replaced.Pixels.Slice(y * region.Width, region.Width).CopyTo(target.Pixels.Slice(row, region.Width));
            }
        }
    }

    private static bool Overlaps(SKRect bounds, SKRectI region) =>
        bounds.Left < region.Right && bounds.Right > region.Left && bounds.Top < region.Bottom && bounds.Bottom > region.Top;

    private static SKRectI Union(IReadOnlyList<SKRectI> regions)
    {
        var union = regions[0];
        foreach (var region in regions)
        {
            union = SKRectI.Union(union, region);
        }

        return union;
    }
}
