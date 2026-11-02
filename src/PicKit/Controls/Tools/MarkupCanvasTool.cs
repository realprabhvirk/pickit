using System.Windows.Input;
using PicKit.Core.Markup;
using PicKit.Core.Rendering;
using SkiaSharp;

namespace PicKit.Controls.Tools;

public enum MarkupMode
{
    Select,
    Pen,
    Highlight,
    Line,
    Arrow,
    Rectangle,
    Ellipse,
    Text,
}

/// <summary>What a markup gesture needs from its tool, so the gesture logic stays independent of the UI.</summary>
public interface IMarkupHost
{
    MarkupMode Mode { get; }

    MarkupStyle CurrentStyle { get; }

    MarkupElement? Selected { get; }

    IReadOnlyList<MarkupElement> Elements { get; }

    void Commit(MarkupElement element);

    void Select(MarkupElement? element);

    void MoveSelected(float deltaX, float deltaY);

    void FinishMove();

    void BeginText(SKPoint position, TextElement? existing);
}

/// <summary>
/// Turns pointer gestures into markup. Work in progress is drawn from a private preview element,
/// and only finished gestures reach the document, so each stroke is exactly one undo step.
/// </summary>
public sealed class MarkupCanvasTool : ICanvasTool
{
    private const float PickReach = 6;
    private const float MinimumPointSpacing = 1.5f;
    private const float MinimumShapeSize = 2;

    private readonly IMarkupHost _host;
    private readonly List<SKPoint> _points = [];
    private SKPoint _start;
    private SKPoint _dragOrigin;
    private bool _isMoving;
    private bool _moved;
    private MarkupElement? _preview;

    public MarkupCanvasTool(IMarkupHost host)
    {
        _host = host;
    }

    public void OnPointerDown(CanvasPointer pointer)
    {
        _start = pointer.Image;
        _points.Clear();
        _preview = null;

        switch (_host.Mode)
        {
            case MarkupMode.Select:
                BeginSelection(pointer);
                break;
            case MarkupMode.Text:
                var existing = TopElementAt(pointer) as TextElement;
                _host.BeginText(pointer.Image, existing);
                break;
            case MarkupMode.Pen or MarkupMode.Highlight:
                _points.Add(pointer.Image);
                _preview = BuildStroke();
                break;
        }
    }

    public void OnPointerMove(CanvasPointer pointer, bool isPressed)
    {
        if (!isPressed)
        {
            return;
        }

        switch (_host.Mode)
        {
            case MarkupMode.Select when _isMoving:
                var delta = pointer.Image - _dragOrigin;
                _host.MoveSelected(delta.X, delta.Y);
                _dragOrigin = pointer.Image;
                _moved = true;
                break;
            case MarkupMode.Pen or MarkupMode.Highlight:
                var last = _points.Count > 0 ? _points[^1] : pointer.Image;
                if (SKPoint.Distance(last, pointer.Image) >= MinimumPointSpacing / pointer.Zoom)
                {
                    _points.Add(pointer.Image);
                    _preview = BuildStroke();
                }

                break;
            case MarkupMode.Line or MarkupMode.Arrow or MarkupMode.Rectangle or MarkupMode.Ellipse:
                _preview = BuildShape(pointer);
                break;
        }
    }

    public void OnPointerUp(CanvasPointer pointer)
    {
        if (_preview is { } finished && IsWorthKeeping(finished, pointer.Zoom))
        {
            _host.Commit(finished);
        }

        if (_isMoving && _moved)
        {
            _host.FinishMove();
        }

        _preview = null;
        _points.Clear();
        _isMoving = false;
    }

    public Cursor? CursorAt(CanvasPointer pointer)
    {
        if (_host.Mode == MarkupMode.Select)
        {
            return TopElementAt(pointer) is null ? null : Cursors.SizeAll;
        }

        return _host.Mode == MarkupMode.Text ? Cursors.IBeam : Cursors.Cross;
    }

    public void DrawOnImage(SKCanvas canvas) => _preview?.Draw(canvas);

    public void DrawOverlay(SKCanvas canvas, ViewTransform view)
    {
        if (_host.Selected is not { } selected)
        {
            return;
        }

        var box = view.ImageToView(selected.Bounds);
        var scale = view.DeviceScale;
        using var dash = SKPathEffect.CreateDash([4 * scale, 3 * scale], 0);
        using var light = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * scale };
        using var dark = new SKPaint { Color = new SKColor(0, 0x67, 0xC0), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * scale, PathEffect = dash };
        canvas.DrawRect(box, light);
        canvas.DrawRect(box, dark);
    }

    private void BeginSelection(CanvasPointer pointer)
    {
        var hit = TopElementAt(pointer);
        _host.Select(hit);
        _isMoving = hit is not null;
        _moved = false;
        _dragOrigin = pointer.Image;
    }

    private MarkupElement? TopElementAt(CanvasPointer pointer)
    {
        var tolerance = PickReach / pointer.Zoom;
        for (var i = _host.Elements.Count - 1; i >= 0; i--)
        {
            if (_host.Elements[i].HitTest(pointer.Image, tolerance))
            {
                return _host.Elements[i];
            }
        }

        return null;
    }

    private StrokeElement BuildStroke() => new()
    {
        Style = _host.CurrentStyle,
        Points = [.. _points],
        IsHighlighter = _host.Mode == MarkupMode.Highlight,
    };

    private MarkupElement BuildShape(CanvasPointer pointer)
    {
        var end = pointer.Image;
        var constrained = pointer.Modifiers.HasFlag(ModifierKeys.Shift);
        var style = _host.CurrentStyle;

        if (_host.Mode is MarkupMode.Line or MarkupMode.Arrow)
        {
            return new LineElement
            {
                Style = style,
                Start = _start,
                End = constrained ? SnapToAngle(_start, end) : end,
                HasArrowhead = _host.Mode == MarkupMode.Arrow,
            };
        }

        var width = end.X - _start.X;
        var height = end.Y - _start.Y;
        if (constrained)
        {
            var side = Math.Max(Math.Abs(width), Math.Abs(height));
            width = MathF.CopySign(side, width);
            height = MathF.CopySign(side, height);
        }

        return new ShapeElement
        {
            Style = style,
            Kind = _host.Mode == MarkupMode.Rectangle ? ShapeKind.Rectangle : ShapeKind.Ellipse,
            Box = new SKRect(
                Math.Min(_start.X, _start.X + width),
                Math.Min(_start.Y, _start.Y + height),
                Math.Max(_start.X, _start.X + width),
                Math.Max(_start.Y, _start.Y + height)),
        };
    }

    private static SKPoint SnapToAngle(SKPoint start, SKPoint end)
    {
        const double step = Math.PI / 4;
        var length = SKPoint.Distance(start, end);
        var angle = Math.Round(Math.Atan2(end.Y - start.Y, end.X - start.X) / step) * step;
        return new SKPoint(start.X + (length * (float)Math.Cos(angle)), start.Y + (length * (float)Math.Sin(angle)));
    }

    private static bool IsWorthKeeping(MarkupElement element, float zoom)
    {
        var minimum = MinimumShapeSize / zoom;
        return element switch
        {
            LineElement line => SKPoint.Distance(line.Start, line.End) >= minimum,
            ShapeElement shape => shape.Box.Width >= minimum && shape.Box.Height >= minimum,
            _ => true,
        };
    }
}
