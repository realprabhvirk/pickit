using PicKit.Core.Imaging;
using SkiaSharp;

namespace PicKit.Core.Segmentation;

public enum BrushMode
{
    Keep,
    Remove,
}

/// <summary>
/// The working mask, with its own small undo history. Brush strokes never touch the image, only
/// this mask, so a "Remove" stroke painted by mistake can be taken back with "Keep" at any time.
/// </summary>
public sealed class MaskEditor : IDisposable
{
    private const float SoftEdge = 0.18f;
    private const int MaxHistory = 48;

    private readonly ImageBuffer _automatic;
    private readonly ImageBuffer _beforeStroke;
    private readonly List<MaskPatch> _undo = [];
    private readonly List<MaskPatch> _redo = [];
    private readonly List<SKPoint> _stroke = [];
    private BrushMode _strokeMode;
    private float _strokeRadius;

    public MaskEditor(ImageBuffer automaticMask)
    {
        _automatic = automaticMask;
        Mask = automaticMask.Clone();
        _beforeStroke = automaticMask.Clone();
    }

    public event Action<SKRectI>? Changed;

    public ImageBuffer Mask { get; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>A mask that keeps everything, for painting by hand.</summary>
    public static ImageBuffer KeepEverything(int width, int height)
    {
        var mask = ImageBuffer.Create(width, height);
        mask.Bitmap.Erase(SKColors.White);
        return mask;
    }

    public void BeginStroke(SKPoint start, float radius, BrushMode mode)
    {
        Mask.Pixels.CopyTo(_beforeStroke.Pixels);
        _stroke.Clear();
        _stroke.Add(start);
        _strokeMode = mode;
        _strokeRadius = radius;
        RedrawStroke();
    }

    public void ContinueStroke(SKPoint point)
    {
        _stroke.Add(point);
        RedrawStroke();
    }

    public void EndStroke()
    {
        if (_stroke.Count == 0)
        {
            return;
        }

        var region = StrokeBounds();
        _stroke.Clear();
        if (region.IsEmpty)
        {
            return;
        }

        foreach (var patch in _redo)
        {
            patch.Dispose();
        }

        _redo.Clear();
        Remember(new MaskPatch(PixelOps.CopyRegion(_beforeStroke, region), region));
    }

    /// <summary>Puts the automatic result back, as one undoable step.</summary>
    public void Restore()
    {
        Mask.Pixels.CopyTo(_beforeStroke.Pixels);
        _automatic.Pixels.CopyTo(Mask.Pixels);
        foreach (var patch in _redo)
        {
            patch.Dispose();
        }

        _redo.Clear();
        Remember(new MaskPatch(_beforeStroke.Clone(), Mask.Bounds));
        Changed?.Invoke(Mask.Bounds);
    }

    public void Undo() => Exchange(_undo, _redo);

    public void Redo() => Exchange(_redo, _undo);

    public void Dispose()
    {
        foreach (var patch in _undo.Concat(_redo))
        {
            patch.Dispose();
        }

        _undo.Clear();
        _redo.Clear();
        _automatic.Dispose();
        _beforeStroke.Dispose();
        Mask.Dispose();
    }

    private void Remember(MaskPatch patch)
    {
        _undo.Add(patch);
        if (_undo.Count > MaxHistory)
        {
            _undo[0].Dispose();
            _undo.RemoveAt(0);
        }
    }

    private void Exchange(List<MaskPatch> from, List<MaskPatch> to)
    {
        if (from.Count == 0)
        {
            return;
        }

        var patch = from[^1];
        from.RemoveAt(from.Count - 1);
        PixelOps.SwapRegion(Mask, patch.Pixels, new SKPointI(patch.Region.Left, patch.Region.Top));
        to.Add(patch);
        Changed?.Invoke(patch.Region);
    }

    private void RedrawStroke()
    {
        // Each move repaints the whole stroke over the pre-stroke mask, so overlapping
        // parts of one soft stroke don't build up, and it stays perfectly smooth.
        var region = StrokeBounds();
        if (region.IsEmpty)
        {
            return;
        }

        for (var y = region.Top; y < region.Bottom; y++)
        {
            _beforeStroke.Row(y).Slice(region.Left, region.Width).CopyTo(Mask.Row(y).Slice(region.Left, region.Width));
        }

        using var canvas = Mask.CreateCanvas();
        canvas.ClipRect(new SKRect(region.Left, region.Top, region.Right, region.Bottom));

        using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(_strokeRadius * SoftEdge, 0.5f));
        using var paint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = _strokeRadius * 2,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
            IsAntialias = true,
            MaskFilter = blur,
            BlendMode = _strokeMode == BrushMode.Keep ? SKBlendMode.SrcOver : SKBlendMode.DstOut,
        };

        using var builder = new SKPathBuilder();
        builder.MoveTo(_stroke[0]);
        builder.LineTo(_stroke.Count == 1 ? new SKPoint(_stroke[0].X + 0.01f, _stroke[0].Y) : _stroke[1]);
        for (var i = 2; i < _stroke.Count; i++)
        {
            builder.LineTo(_stroke[i]);
        }

        using var path = builder.Detach();
        canvas.DrawPath(path, paint);

        Changed?.Invoke(region);
    }

    private SKRectI StrokeBounds()
    {
        var reach = _strokeRadius * (1 + (SoftEdge * 3)) + 2;
        var (left, top, right, bottom) = (float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
        foreach (var point in _stroke)
        {
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }

        return ImageOps.CoveringPixels(new SKRect(left - reach, top - reach, right + reach, bottom + reach), Mask.Width, Mask.Height);
    }

    private sealed record MaskPatch(ImageBuffer Pixels, SKRectI Region) : IDisposable
    {
        public void Dispose() => Pixels.Dispose();
    }
}
