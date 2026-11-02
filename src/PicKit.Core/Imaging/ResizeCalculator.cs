using SkiaSharp;

namespace PicKit.Core.Imaging;

public enum ResizeMode
{
    /// <summary>Exactly the requested size. Nothing is cropped, though proportions may change.</summary>
    Scale,

    /// <summary>As large as possible inside the requested size, without cropping or enlarging.</summary>
    Fit,

    /// <summary>Explicitly crops to the requested shape, then scales to the requested size.</summary>
    CropToFill,
}

/// <summary>What a resize will do, worked out before any pixels are touched.</summary>
public readonly record struct ResizePlan(int Width, int Height, SKRectI? Crop)
{
    public bool RequiresCrop => Crop is not null;
}

public static class ResizeCalculator
{
    public static ResizePlan Plan(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, ResizeMode mode)
    {
        Validate(sourceWidth, sourceHeight);
        Validate(targetWidth, targetHeight);

        return mode switch
        {
            ResizeMode.Scale => new ResizePlan(targetWidth, targetHeight, null),
            ResizeMode.Fit => PlanFit(sourceWidth, sourceHeight, targetWidth, targetHeight),
            ResizeMode.CropToFill => PlanCrop(sourceWidth, sourceHeight, targetWidth, targetHeight),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    public static int HeightForWidth(int sourceWidth, int sourceHeight, int width) =>
        Math.Max(1, (int)Math.Round(width * (double)sourceHeight / sourceWidth));

    public static int WidthForHeight(int sourceWidth, int sourceHeight, int height) =>
        Math.Max(1, (int)Math.Round(height * (double)sourceWidth / sourceHeight));

    /// <summary>True if these dimensions would visibly distort the image's proportions.</summary>
    public static bool IsStretched(int sourceWidth, int sourceHeight, int width, int height)
    {
        const double tolerance = 0.01;
        var sourceRatio = (double)sourceWidth / sourceHeight;
        var targetRatio = (double)width / height;

        // Rounding to whole pixels alone can shift the ratio on small images.
        var heightForRatio = HeightForWidth(sourceWidth, sourceHeight, width);
        return Math.Abs(heightForRatio - height) > 1
            && Math.Abs(sourceRatio - targetRatio) / sourceRatio > tolerance;
    }

    /// <summary>The largest centred rectangle with the target's shape.</summary>
    public static SKRectI CentredCrop(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var targetRatio = (double)targetWidth / targetHeight;
        var sourceRatio = (double)sourceWidth / sourceHeight;

        int width;
        int height;
        if (sourceRatio > targetRatio)
        {
            height = sourceHeight;
            width = Math.Clamp((int)Math.Round(sourceHeight * targetRatio), 1, sourceWidth);
        }
        else
        {
            width = sourceWidth;
            height = Math.Clamp((int)Math.Round(sourceWidth / targetRatio), 1, sourceHeight);
        }

        var left = (sourceWidth - width) / 2;
        var top = (sourceHeight - height) / 2;
        return new SKRectI(left, top, left + width, top + height);
    }

    private static ResizePlan PlanFit(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var ratio = Math.Min((double)targetWidth / sourceWidth, (double)targetHeight / sourceHeight);
        if (ratio >= 1)
        {
            return new ResizePlan(sourceWidth, sourceHeight, null);
        }

        var width = Math.Clamp((int)Math.Round(sourceWidth * ratio), 1, targetWidth);
        var height = Math.Clamp((int)Math.Round(sourceHeight * ratio), 1, targetHeight);
        return new ResizePlan(width, height, null);
    }

    private static ResizePlan PlanCrop(int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var crop = CentredCrop(sourceWidth, sourceHeight, targetWidth, targetHeight);
        var alreadyThatShape = crop.Width == sourceWidth && crop.Height == sourceHeight;
        return new ResizePlan(targetWidth, targetHeight, alreadyThatShape ? null : crop);
    }

    private static void Validate(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Dimensions must be at least 1 pixel.");
        }
    }
}
