using System.Windows.Input;
using PicKit.Core.Rendering;
using SkiaSharp;

namespace PicKit.Controls.Tools;

/// <summary>Lets the user drag out a rectangle over the image.</summary>
public sealed class RegionCanvasTool : ICanvasTool
{
    private const float MinimumDrag = 3;

    private readonly Action<SKRect> _onRegionDrawn;
    private readonly Action<SKCanvas>? _drawPending;
    private SKPoint _start;
    private SKPoint _current;
    private bool _isDragging;

    public RegionCanvasTool(Action<SKRect> onRegionDrawn, Action<SKCanvas>? drawPending = null)
    {
        _onRegionDrawn = onRegionDrawn;
        _drawPending = drawPending;
    }

    private SKRect Current => new(
        Math.Min(_start.X, _current.X),
        Math.Min(_start.Y, _current.Y),
        Math.Max(_start.X, _current.X),
        Math.Max(_start.Y, _current.Y));

    public void OnPointerDown(CanvasPointer pointer)
    {
        _start = pointer.Image;
        _current = pointer.Image;
        _isDragging = true;
    }

    public void OnPointerMove(CanvasPointer pointer, bool isPressed)
    {
        if (isPressed && _isDragging)
        {
            _current = pointer.Image;
        }
    }

    public void OnPointerUp(CanvasPointer pointer)
    {
        _current = pointer.Image;
        _isDragging = false;

        var box = Current;
        if (box.Width * pointer.Zoom >= MinimumDrag && box.Height * pointer.Zoom >= MinimumDrag)
        {
            _onRegionDrawn(box);
        }
    }

    public Cursor? CursorAt(CanvasPointer pointer) => Cursors.Cross;

    public void DrawOnImage(SKCanvas canvas) => _drawPending?.Invoke(canvas);

    public void DrawOverlay(SKCanvas canvas, ViewTransform view)
    {
        if (!_isDragging)
        {
            return;
        }

        var box = view.ImageToView(Current);
        var scale = view.DeviceScale;
        using var fill = new SKPaint { Color = new SKColor(0, 0x67, 0xC0, 50) };
        using var edge = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * scale };
        using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 120), Style = SKPaintStyle.Stroke, StrokeWidth = 3f * scale };
        canvas.DrawRect(box, fill);
        canvas.DrawRect(box, shadow);
        canvas.DrawRect(box, edge);
    }
}
