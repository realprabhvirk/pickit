using PicKit.Core.Imaging;
using SkiaSharp;

namespace PicKit.Tests;

internal static class TestImages
{
    /// <summary>Every pixel gets a unique opaque colour, so any misplaced pixel is detectable.</summary>
    public static ImageBuffer Numbered(int width, int height)
    {
        var buffer = ImageBuffer.Create(width, height);
        var pixels = buffer.Pixels;
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = 0xFF000000 | (uint)(i + 1);
        }

        return buffer;
    }

    public static ImageBuffer Solid(int width, int height, SKColor color)
    {
        var buffer = ImageBuffer.Create(width, height);
        using var canvas = buffer.CreateCanvas();
        canvas.Clear(color);
        return buffer;
    }

    /// <summary>Gradients plus noise: compresses like a photo, unlike a flat colour.</summary>
    public static ImageBuffer Photo(int width, int height, int seed = 7)
    {
        var random = new Random(seed);
        var buffer = ImageBuffer.Create(width, height);
        var pixels = buffer.Pixels;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var red = (x * 255 / width) + random.Next(-24, 24);
                var green = (y * 255 / height) + random.Next(-24, 24);
                var blue = ((x + y) * 255 / (width + height)) + random.Next(-24, 24);
                pixels[(y * width) + x] = 0xFF000000
                    | ((uint)Math.Clamp(red, 0, 255) << 16)
                    | ((uint)Math.Clamp(green, 0, 255) << 8)
                    | (uint)Math.Clamp(blue, 0, 255);
            }
        }

        return buffer;
    }

    public static string TempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PicKitTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
