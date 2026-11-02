using System.Windows.Input;
using PicKit.Core.Imaging;
using PicKit.Core.Rendering;
using SkiaSharp;

namespace PicKit.Controls.Tools;

/// <summary>
/// The crop box over the image. It always shows exactly what will remain, and everything else
/// is dimmed. Nothing is cut until the owning tool applies the box.
/// </summary>
public sealed class CropCanvasTool : ICanvasTool
{
    private const float HandleReach = 14;
    private const float HandleSize = 10;
    private const byte DimAlpha = 150;

    private int _imageWidth = 1;
    private int _imageHeight = 1;
    private CropHandle _active = CropHandle.None;
    private SKRect _dragStartBox;
    private SKPoint _dragStartPoint;

    public event Action? BoxChanged;

    public SKRect Box { get; private set; }

    public float? Aspect { get; private set; }

    public SKRectI PixelBox => CropGeometry.ToPixels(Box, _imageWidth, _imageHeight);

    public void Reset(int imageWidth, int imageHeight, float? aspect)
    {
        _imageWidth = imageWidth;
        _imageHeight = imageHeight;
        Aspect = aspect;
        Box = CropGeometry.Initial(imageWidth, imageHeight, aspect);
        BoxChanged?.Invoke();
    }

    public void SetAspect(float? aspect)
    {
        Aspect = aspect;
        if (aspect is { } ratio)
        {
            Box = CropGeometry.WithAspect(Box, ratio, _imageWidth, _imageHeight);
        }

        BoxChanged?.Invoke();
    }

    public void OnPointerDown(CanvasPointer pointer)
    {
        _active = HandleAt(pointer);
        _dragStartBox = Box;
        _dragStartPoint = pointer.Image;
    }

    public void OnPointerMove(CanvasPointer pointer, bool isPressed)
    {
        if (!isPressed || _active == CropHandle.None)
        {
            return;
        }

        var delta = new SKPoint(pointer.Image.X - _dragStartPoint.X, pointer.Image.Y - _dragStartPoint.Y);
        Box = CropGeometry.Drag(_dragStartBox, _active, delta, Aspect, _imageWidth, _imageHeight);
        BoxChanged?.Invoke();
    }

    public void OnPointerUp(CanvasPointer pointer) => _active = CropHandle.None;

    public Cursor? CursorAt(CanvasPointer pointer) =>
        (_active != CropHandle.None ? _active : HandleAt(pointer)) switch
        {
            CropHandle.Move => Cursors.SizeAll,
            CropHandle.TopLeft or CropHandle.BottomRight => Cursors.SizeNWSE,
            CropHandle.TopRight or CropHandle.BottomLeft => Cursors.SizeNESW,
            CropHandle.Left or CropHandle.Right => Cursors.SizeWE,
            CropHandle.Top or CropHandle.Bottom => Cursors.SizeNS,
            _ => null,
        };

    public void DrawOverlay(SKCanvas canvas, ViewTransform view)
    {
        var image = view.ImageToView(new SKRect(0, 0, _imageWidth, _imageHeight));
        var box = view.ImageToView(Box);
        var scale = view.DeviceScale;

        using (var builder = new SKPathBuilder())
        {
            builder.AddRect(image);
            builder.AddRect(box);
            builder.FillType = SKPathFillType.EvenOdd;
            using var outside = builder.Detach();
            using var dim = new SKPaint { Color = new SKColor(0, 0, 0, DimAlpha), IsAntialias = false };
            canvas.DrawPath(outside, dim);
        }

        using var line = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * scale, IsAntialias = true };
        using var outline = new SKPaint { Color = new SKColor(0, 0, 0, 120), Style = SKPaintStyle.Stroke, StrokeWidth = 3f * scale, IsAntialias = true };
        canvas.DrawRect(box, outline);
        canvas.DrawRect(box, line);

        if (_active != CropHandle.None)
        {
            DrawThirds(canvas, box, scale);
        }

        DrawHandles(canvas, box, scale);
    }

    private static void DrawThirds(SKCanvas canvas, SKRect box, float scale)
    {
        using var guide = new SKPaint { Color = new SKColor(255, 255, 255, 170), Style = SKPaintStyle.Stroke, StrokeWidth = 1f * scale };
        for (var i = 1; i < 3; i++)
        {
            var x = box.Left + (box.Width * i / 3);
            var y = box.Top + (box.Height * i / 3);
            canvas.DrawLine(x, box.Top, x, box.Bottom, guide);
            canvas.DrawLine(box.Left, y, box.Right, y, guide);
        }
    }

    private static void DrawHandles(SKCanvas canvas, SKRect box, float scale)
    {
        using var fill = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var edge = new SKPaint { Color = new SKColor(0, 0, 0, 150), Style = SKPaintStyle.Stroke, StrokeWidth = 1f * scale, IsAntialias = true };
        var half = HandleSize * scale / 2;

        void Handle(float x, float y)
        {
            var rect = new SKRect(x - half, y - half, x + half, y + half);
            canvas.DrawRoundRect(rect, 2 * scale, 2 * scale, fill);
            canvas.DrawRoundRect(rect, 2 * scale, 2 * scale, edge);
        }

        Handle(box.Left, box.Top);
        Handle(box.MidX, box.Top);
        Handle(box.Right, box.Top);
        Handle(box.Right, box.MidY);
        Handle(box.Right, box.Bottom);
        Handle(box.MidX, box.Bottom);
        Handle(box.Left, box.Bottom);
        Handle(box.Left, box.MidY);
    }

    private CropHandle HandleAt(CanvasPointer pointer) =>
        CropGeometry.HitTest(Box, pointer.Image, HandleReach / pointer.Zoom);
}
