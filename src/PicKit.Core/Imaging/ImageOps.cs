using SkiaSharp;

namespace PicKit.Core.Imaging;

/// <summary>Whole-image and region operations. Each returns new pixels and leaves its input alone.</summary>
public static class ImageOps
{
    public static ImageBuffer Crop(ImageBuffer source, SKRectI region)
    {
        var bounded = SKRectI.Intersect(region, source.Bounds);
        if (bounded.IsEmpty)
        {
            throw new ArgumentOutOfRangeException(nameof(region), "The crop lies outside the image.");
        }

        return PixelOps.CopyRegion(source, bounded);
    }

    /// <summary>High-quality scaling, averaging down first so large reductions don't shimmer.</summary>
    public static ImageBuffer Resize(ImageBuffer source, int width, int height)
    {
        ImageLimits.EnsureFits(width, height);
        if (width == source.Width && height == source.Height)
        {
            return source.Clone();
        }

        var current = source;
        try
        {
            while (current.Width >= width * 2 && current.Height >= height * 2)
            {
                var half = PixelOps.Halve(current);
                if (!ReferenceEquals(current, source))
                {
                    current.Dispose();
                }

                current = half;
            }

            var target = ImageBuffer.Create(width, height);
            var enlarging = width > source.Width || height > source.Height;
            var sampling = new SKSamplingOptions(enlarging ? SKCubicResampler.CatmullRom : SKCubicResampler.Mitchell);
            using var from = current.Bitmap.PeekPixels();
            using var to = target.Bitmap.PeekPixels();
            if (!from.ScalePixels(to, sampling))
            {
                target.Dispose();
                throw new ImageException(ImageErrorKind.EncodeFailed, "PicKit couldn't resize this image.");
            }

            return target;
        }
        finally
        {
            if (!ReferenceEquals(current, source))
            {
                current.Dispose();
            }
        }
    }

    public static ImageBuffer ApplyPlan(ImageBuffer source, ResizePlan plan)
    {
        if (plan.Crop is not { } crop)
        {
            return Resize(source, plan.Width, plan.Height);
        }

        using var cropped = Crop(source, crop);
        return Resize(cropped, plan.Width, plan.Height);
    }

    /// <summary>Composites over a solid colour, leaving a fully opaque image.</summary>
    public static ImageBuffer Flatten(ImageBuffer source, SKColor background)
    {
        var target = ImageBuffer.Create(source.Width, source.Height);
        using var canvas = target.CreateCanvas();
        canvas.Clear(background.WithAlpha(255));
        using var image = source.AsImage();
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
        return target;
    }

    /// <summary>
    /// Blurred replacement pixels for a region. Only the region's own pixels are sampled, so
    /// nothing around it bleeds in and the surrounding image is left untouched.
    /// </summary>
    public static ImageBuffer BlurRegion(ImageBuffer source, SKRectI region, float sigma)
    {
        using var original = PixelOps.CopyRegion(source, region);
        var target = ImageBuffer.Create(region.Width, region.Height);

        using var canvas = target.CreateCanvas();
        using var filter = SKImageFilter.CreateBlur(sigma, sigma, SKShaderTileMode.Clamp, null);
        using var paint = new SKPaint { ImageFilter = filter };
        using var image = original.AsImage();
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), paint);
        return target;
    }

    /// <summary>
    /// A region's original pixels with <paramref name="replaced"/> showing through a shape, so an
    /// effect reaches exactly that shape and nothing else in the region changes.
    /// </summary>
    public static ImageBuffer ThroughOutline(ImageBuffer source, SKRectI region, ImageBuffer replaced, RegionOutline outline)
    {
        var target = PixelOps.CopyRegion(source, region);
        using var canvas = target.CreateCanvas();
        canvas.Translate(-region.Left, -region.Top);

        using var image = replaced.AsImage();
        using var shader = image.ToShader(
            SKShaderTileMode.Clamp,
            SKShaderTileMode.Clamp,
            new SKSamplingOptions(SKFilterMode.Nearest),
            SKMatrix.CreateTranslation(region.Left, region.Top));

        using var paint = new SKPaint
        {
            Shader = shader,
            BlendMode = SKBlendMode.Src,
            IsAntialias = true,
            Style = outline.StrokeWidth > 0 ? SKPaintStyle.Stroke : SKPaintStyle.Fill,
            StrokeWidth = outline.StrokeWidth,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };

        canvas.DrawPath(outline.Path, paint);
        return target;
    }

    /// <summary>Mosaic replacement pixels: every block becomes the average of its pixels.</summary>
    public static ImageBuffer PixelateRegion(ImageBuffer source, SKRectI region, int blockSize)
    {
        blockSize = Math.Max(2, blockSize);
        var target = PixelOps.CopyRegion(source, region);
        var rowCount = ((region.Height - 1) / blockSize) + 1;

        Parallel.For(0, rowCount, blockRow =>
        {
            var pixels = target.Pixels;
            var top = blockRow * blockSize;
            var bottom = Math.Min(top + blockSize, region.Height);
            for (var left = 0; left < region.Width; left += blockSize)
            {
                var right = Math.Min(left + blockSize, region.Width);
                FillBlockWithAverage(pixels, region.Width, left, top, right, bottom);
            }
        });

        return target;
    }

    /// <summary>Solid replacement pixels. There is nothing of the original left in them.</summary>
    public static ImageBuffer SolidRegion(int width, int height, SKColor color)
    {
        var target = ImageBuffer.Create(width, height);
        target.Bitmap.Erase(color.WithAlpha(255));
        return target;
    }

    /// <summary>Multiplies each pixel by a mask's alpha, leaving real transparency.</summary>
    public static ImageBuffer ApplyMask(ImageBuffer source, ImageBuffer mask)
    {
        var target = ImageBuffer.Create(source.Width, source.Height);
        Parallel.For(0, source.Height, y =>
        {
            var from = source.Row(y);
            var keep = mask.Row(y);
            var to = target.Row(y);
            for (var x = 0; x < from.Length; x++)
            {
                to[x] = Scale(from[x], keep[x] >> 24);
            }
        });

        return target;
    }

    /// <summary>Expands a box to cover every pixel it touches, so a privacy edit can't miss an edge.</summary>
    public static SKRectI CoveringPixels(SKRect box, int width, int height)
    {
        var left = Math.Clamp((int)Math.Floor(Math.Min(box.Left, box.Right)), 0, width);
        var top = Math.Clamp((int)Math.Floor(Math.Min(box.Top, box.Bottom)), 0, height);
        var right = Math.Clamp((int)Math.Ceiling(Math.Max(box.Left, box.Right)), 0, width);
        var bottom = Math.Clamp((int)Math.Ceiling(Math.Max(box.Top, box.Bottom)), 0, height);
        return new SKRectI(left, top, right, bottom);
    }

    private static uint Scale(uint pixel, uint amount)
    {
        if (amount == 255)
        {
            return pixel;
        }

        const uint evenChannels = 0x00FF00FF;
        var even = (((pixel & evenChannels) * amount) + 0x00800080) >> 8;
        var odd = ((((pixel >> 8) & evenChannels) * amount) + 0x00800080) >> 8;
        return (even & evenChannels) | ((odd & evenChannels) << 8);
    }

    private static void FillBlockWithAverage(Span<uint> pixels, int stride, int left, int top, int right, int bottom)
    {
        ulong blue = 0;
        ulong green = 0;
        ulong red = 0;
        ulong alpha = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var pixel = pixels[(y * stride) + x];
                blue += pixel & 0xFF;
                green += (pixel >> 8) & 0xFF;
                red += (pixel >> 16) & 0xFF;
                alpha += pixel >> 24;
            }
        }

        var count = (ulong)((right - left) * (bottom - top));
        var average = (uint)(((alpha / count) << 24) | ((red / count) << 16) | ((green / count) << 8) | (blue / count));
        for (var y = top; y < bottom; y++)
        {
            pixels.Slice((y * stride) + left, right - left).Fill(average);
        }
    }
}
