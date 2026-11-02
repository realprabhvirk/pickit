using System.Windows.Input;
using PicKit.Core.Rendering;
using PicKit.Core.Segmentation;
using SkiaSharp;

namespace PicKit.Controls.Tools;

/// <summary>
/// Shows the image through the mask, so checkers appear wherever it will be transparent,
/// and turns drags into brush strokes on the mask.
/// </summary>
public sealed class MaskBrushCanvasTool : ICanvasTool, IDisposable
{
    private readonly MaskEditor _mask;
    private readonly Func<BrushMode> _mode;
    private readonly Func<float> _radius;
    private readonly DisplayPyramid _maskPyramid;
    private SKPoint _hover;
    private bool _hasHover;
    private bool _isPainting;

    public MaskBrushCanvasTool(MaskEditor mask, Func<BrushMode> mode, Func<float> radius)
    {
        _mask = mask;
        _mode = mode;
        _radius = radius;
        _maskPyramid = new DisplayPyramid(mask.Mask);
        _mask.Changed += _maskPyramid.Update;
    }

    public void OnPointerDown(CanvasPointer pointer)
    {
        _isPainting = true;
        _mask.BeginStroke(pointer.Image, _radius(), _mode());
    }

    public void OnPointerMove(CanvasPointer pointer, bool isPressed)
    {
        _hover = pointer.Image;
        _hasHover = true;
        if (isPressed && _isPainting)
        {
            _mask.ContinueStroke(pointer.Image);
        }
    }

    public void OnPointerUp(CanvasPointer pointer)
    {
        _isPainting = false;
        _mask.EndStroke();
    }

    public Cursor? CursorAt(CanvasPointer pointer) => Cursors.None;

    public bool DrawImage(SKCanvas canvas, SKImage image, SKRect bounds, SKSamplingOptions sampling, double zoom)
    {
        canvas.SaveLayer(bounds, null);
        canvas.DrawImage(image, bounds, sampling);

        using var mask = _maskPyramid.ForZoom(zoom).AsImage();
        using var keepOnlyMasked = new SKPaint { BlendMode = SKBlendMode.DstIn };
        canvas.DrawImage(mask, bounds, sampling, keepOnlyMasked);
        canvas.Restore();
        return true;
    }

    public void DrawOverlay(SKCanvas canvas, ViewTransform view)
    {
        if (!_hasHover)
        {
            return;
        }

        var centre = view.ImageToView(_hover);
        var radius = (float)(_radius() * view.Zoom);
        var scale = view.DeviceScale;
        using var dark = new SKPaint { Color = new SKColor(0, 0, 0, 170), Style = SKPaintStyle.Stroke, StrokeWidth = 3 * scale, IsAntialias = true };
        using var light = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * scale, IsAntialias = true };
        canvas.DrawCircle(centre, radius, dark);
        canvas.DrawCircle(centre, radius, light);
    }

    public void Dispose()
    {
        _mask.Changed -= _maskPyramid.Update;
        _maskPyramid.Dispose();
    }
}
