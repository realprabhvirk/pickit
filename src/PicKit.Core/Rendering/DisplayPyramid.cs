using PicKit.Core.Imaging;
using SkiaSharp;

namespace PicKit.Core.Rendering;

/// <summary>
/// Half-size copies of the image, used only for display. Drawing from the nearest level keeps
/// zoomed-out views sharp and fast, at a cost of about a third of the image's memory.
/// </summary>
public sealed class DisplayPyramid : IDisposable
{
    private const int SmallestSide = 128;

    private readonly ImageBuffer _source;
    private readonly List<ImageBuffer> _levels = [];

    public DisplayPyramid(ImageBuffer source)
    {
        _source = source;
    }

    public int BuiltLevels => _levels.Count;

    /// <summary>Picks the smallest copy that still has at least one pixel per device pixel.</summary>
    public ImageBuffer ForZoom(double zoom)
    {
        var wanted = 0;
        var scale = zoom;
        while (scale <= 0.5 && Math.Max(_source.Width >> (wanted + 1), _source.Height >> (wanted + 1)) >= SmallestSide)
        {
            scale *= 2;
            wanted++;
        }

        return wanted == 0 ? _source : LevelAt(wanted);
    }

    /// <summary>Brings already-built levels up to date after pixels in <paramref name="region"/> changed.</summary>
    public void Update(SKRectI region)
    {
        var parent = _source;
        var changed = region;
        foreach (var level in _levels)
        {
            changed = new SKRectI(changed.Left / 2, changed.Top / 2, (changed.Right + 1) / 2, (changed.Bottom + 1) / 2);
            PixelOps.HalveRegion(parent, level, changed);
            parent = level;
        }
    }

    public void Dispose()
    {
        foreach (var level in _levels)
        {
            level.Dispose();
        }

        _levels.Clear();
    }

    private ImageBuffer LevelAt(int level)
    {
        while (_levels.Count < level)
        {
            _levels.Add(PixelOps.Halve(_levels.Count == 0 ? _source : _levels[^1]));
        }

        return _levels[level - 1];
    }
}
