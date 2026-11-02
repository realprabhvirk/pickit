using System.Windows.Input;
using PicKit.Core.Imaging;
using PicKit.Core.Rendering;
using SkiaSharp;

namespace PicKit.Controls.Tools;

/// <summary>Lets the user mark an area of the image, as a dragged shape or with a brush.</summary>
public sealed class RegionCanvasTool : ICanvasTool
{
    private const float MinimumDrag = 3;
    private const float MinimumPointSpacing = 1.5f;

    private readonly Action<SKRect> _onRegionDrawn;
    private readonly Action<SKCanvas>? _drawPending;
    private readonly List<SKPoint> _points = [];
    private SKPoint _start;
    private SKPoint _current;
    private bool _isDragging;

    public RegionCanvasTool(Action<SKRect> onRegionDrawn, Action<SKCanvas>? drawPending = null)
    {
        _onRegionDrawn = onRegionDrawn;
        _drawPending = drawPending;
    }

    /// <summary>Anything other than a box is reported here, and the receiver owns the outline.</summary>
    public Action<RegionOutline>? OutlineDrawn { get; init; }

    public RegionShape Shape { get; set; } = RegionShape.Box;

    /// <summary>Brush width in image pixels.</summary>
    public float BrushWidth { get; set; } = 40;

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
        _points.Clear();
        _points.Add(pointer.Image);
    }

    public void OnPointerMove(CanvasPointer pointer, bool isPressed)
    {
        _current = pointer.Image;
        if (isPressed && _isDragging && Shape == RegionShape.Brush
            && SKPoint.Distance(_points[^1], pointer.Image) >= MinimumPointSpacing / pointer.Zoom)
        {
            _points.Add(pointer.Image);
        }
    }

    public void OnPointerUp(CanvasPointer pointer)
    {
        _current = pointer.Image;
        _isDragging = false;

        if (Shape == RegionShape.Brush)
        {
            OutlineDrawn?.Invoke(RegionOutline.ForStroke(_points, BrushWidth));
            return;
        }

        var box = Current;
        if (box.Width * pointer.Zoom < MinimumDrag || box.Height * pointer.Zoom < MinimumDrag)
        {
            return;
        }

        if (Shape == RegionShape.Box)
        {
            _onRegionDrawn(box);
        }
        else
        {
            OutlineDrawn?.Invoke(RegionOutline.ForBox(box, Shape));
        }
    }

    public Cursor? CursorAt(CanvasPointer pointer) => Cursors.Cross;

    public void DrawOnImage(SKCanvas canvas) => _drawPending?.Invoke(canvas);

    public void DrawOverlay(SKCanvas canvas, ViewTransform view)
    {
        if (Shape == RegionShape.Brush)
        {
            DrawBrush(canvas, view);
        }
        else if (_isDragging)
        {
            DrawDraggedShape(canvas, view);
        }
    }

    private void DrawBrush(SKCanvas canvas, ViewTransform view)
    {
        var width = BrushWidth * (float)view.Zoom;
        var scale = view.DeviceScale;
        if (_isDragging)
        {
            using var outline = RegionOutline.ForStroke(_points, BrushWidth);
            using var stroke = new SKPaint
            {
                Color = new SKColor(0, 0x67, 0xC0, 90),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = BrushWidth,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true,
            };

            canvas.Save();
            canvas.Concat(view.Matrix);
            canvas.DrawPath(outline.Path, stroke);
            canvas.Restore();
        }

        // The brush's real size under the pointer, so its reach is clear before painting.
        var centre = view.ImageToView(_current);
        using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 120), Style = SKPaintStyle.Stroke, StrokeWidth = 3f * scale, IsAntialias = true };
        using var edge = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * scale, IsAntialias = true };
        canvas.DrawCircle(centre, width / 2, shadow);
        canvas.DrawCircle(centre, width / 2, edge);
    }

    private void DrawDraggedShape(SKCanvas canvas, ViewTransform view)
    {
        var box = view.ImageToView(Current);
        var scale = view.DeviceScale;
        using var fill = new SKPaint { Color = new SKColor(0, 0x67, 0xC0, 50), IsAntialias = true };
        using var edge = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * scale, IsAntialias = true };
        using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 120), Style = SKPaintStyle.Stroke, StrokeWidth = 3f * scale, IsAntialias = true };

        if (Shape == RegionShape.Box)
        {
            canvas.DrawRect(box, fill);
            canvas.DrawRect(box, shadow);
            canvas.DrawRect(box, edge);
            return;
        }

        using var outline = RegionOutline.ForBox(box, Shape);
        canvas.DrawPath(outline.Path, fill);
        canvas.DrawPath(outline.Path, shadow);
        canvas.DrawPath(outline.Path, edge);
    }
}
