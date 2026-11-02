using System.Collections.Immutable;
using SkiaSharp;

namespace PicKit.Core.Markup;

/// <summary>A freehand pen or highlighter stroke.</summary>
public sealed record StrokeElement : MarkupElement
{
    public required ImmutableArray<SKPoint> Points { get; init; }

    /// <summary>A highlighter tints what's beneath it, so dark text stays readable, instead of covering it.</summary>
    public bool IsHighlighter { get; init; }

    public override SKRect Bounds
    {
        get
        {
            var (left, top, right, bottom) = (float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            foreach (var point in Points)
            {
                left = Math.Min(left, point.X);
                top = Math.Min(top, point.Y);
                right = Math.Max(right, point.X);
                bottom = Math.Max(bottom, point.Y);
            }

            var half = Style.Thickness / 2;
            return new SKRect(left - half, top - half, right + half, bottom + half);
        }
    }

    public override void Draw(SKCanvas canvas)
    {
        if (Points.Length == 0)
        {
            return;
        }

        using var paint = StrokePaint(IsHighlighter ? SKStrokeCap.Butt : SKStrokeCap.Round);
        if (IsHighlighter)
        {
            paint.BlendMode = SKBlendMode.Multiply;
        }

        // One path is filled once, so a stroke that crosses itself doesn't darken at the crossing.
        using var path = BuildPath();
        canvas.DrawPath(path, paint);
    }

    public override bool HitTest(SKPoint point, float tolerance)
    {
        var reach = (Style.Thickness / 2) + tolerance;
        if (Points.Length == 1)
        {
            return SKPoint.Distance(point, Points[0]) <= reach;
        }

        for (var i = 1; i < Points.Length; i++)
        {
            if (DistanceToSegment(point, Points[i - 1], Points[i]) <= reach)
            {
                return true;
            }
        }

        return false;
    }

    public override MarkupElement Transformed(SKMatrix matrix) => this with
    {
        Points = [.. Points.Select(matrix.MapPoint)],
        Style = ScaledStyle(matrix),
    };

    private SKPath BuildPath()
    {
        using var builder = new SKPathBuilder();
        builder.MoveTo(Points[0]);

        if (Points.Length == 1)
        {
            // A single click still leaves a dot.
            builder.LineTo(Points[0].X + 0.01f, Points[0].Y);
            return builder.Detach();
        }

        // Curving through midpoints turns jittery mouse samples into a smooth line.
        for (var i = 1; i < Points.Length - 1; i++)
        {
            var middle = new SKPoint((Points[i].X + Points[i + 1].X) / 2, (Points[i].Y + Points[i + 1].Y) / 2);
            builder.QuadTo(Points[i], middle);
        }

        builder.LineTo(Points[^1]);
        return builder.Detach();
    }
}

/// <summary>A straight line, with an optional arrowhead at its end.</summary>
public sealed record LineElement : MarkupElement
{
    private const float HeadLengthPerThickness = 4;
    private const float MinimumHeadLength = 10;
    private const double HeadHalfAngle = Math.PI / 7;

    public required SKPoint Start { get; init; }

    public required SKPoint End { get; init; }

    public bool HasArrowhead { get; init; }

    public override SKRect Bounds
    {
        get
        {
            var reach = HasArrowhead ? HeadLength : Style.Thickness / 2;
            return new SKRect(
                Math.Min(Start.X, End.X) - reach,
                Math.Min(Start.Y, End.Y) - reach,
                Math.Max(Start.X, End.X) + reach,
                Math.Max(Start.Y, End.Y) + reach);
        }
    }

    private float HeadLength => Math.Max(Style.Thickness * HeadLengthPerThickness, MinimumHeadLength);

    public override void Draw(SKCanvas canvas)
    {
        using var paint = StrokePaint();
        if (!HasArrowhead)
        {
            canvas.DrawLine(Start, End, paint);
            return;
        }

        // The shaft stops at the head's base so a translucent arrow has no darker overlap.
        var length = SKPoint.Distance(Start, End);
        var head = Math.Min(HeadLength, length);
        var angle = Math.Atan2(End.Y - Start.Y, End.X - Start.X);
        var baseDistance = head * (float)Math.Cos(HeadHalfAngle);
        var shaftEnd = new SKPoint(
            End.X - (baseDistance * (float)Math.Cos(angle)),
            End.Y - (baseDistance * (float)Math.Sin(angle)));

        if (length > baseDistance)
        {
            canvas.DrawLine(Start, shaftEnd, paint);
        }

        using var builder = new SKPathBuilder();
        builder.MoveTo(End);
        builder.LineTo(HeadCorner(angle, head, 1));
        builder.LineTo(HeadCorner(angle, head, -1));
        builder.Close();
        using var triangle = builder.Detach();

        using var fill = new SKPaint { Color = Style.EffectiveColor, IsAntialias = true };
        canvas.DrawPath(triangle, fill);
    }

    public override bool HitTest(SKPoint point, float tolerance) =>
        DistanceToSegment(point, Start, End) <= (Style.Thickness / 2) + tolerance;

    public override MarkupElement Transformed(SKMatrix matrix) => this with
    {
        Start = matrix.MapPoint(Start),
        End = matrix.MapPoint(End),
        Style = ScaledStyle(matrix),
    };

    private SKPoint HeadCorner(double angle, float head, int side) => new(
        End.X - (head * (float)Math.Cos(angle + (side * HeadHalfAngle))),
        End.Y - (head * (float)Math.Sin(angle + (side * HeadHalfAngle))));
}

public enum ShapeKind
{
    Rectangle,
    Ellipse,
}

/// <summary>An unfilled rectangle or ellipse.</summary>
public sealed record ShapeElement : MarkupElement
{
    public required SKRect Box { get; init; }

    public required ShapeKind Kind { get; init; }

    public override SKRect Bounds
    {
        get
        {
            var half = Style.Thickness / 2;
            return new SKRect(Box.Left - half, Box.Top - half, Box.Right + half, Box.Bottom + half);
        }
    }

    public override void Draw(SKCanvas canvas)
    {
        using var paint = StrokePaint();
        if (Kind == ShapeKind.Rectangle)
        {
            canvas.DrawRect(Box, paint);
        }
        else
        {
            canvas.DrawOval(Box, paint);
        }
    }

    public override bool HitTest(SKPoint point, float tolerance)
    {
        var reach = (Style.Thickness / 2) + tolerance;
        if (Kind == ShapeKind.Rectangle)
        {
            var outer = SKRect.Inflate(Box, reach, reach);
            var inner = SKRect.Inflate(Box, -reach, -reach);
            return outer.Contains(point) && !(inner.Width > 0 && inner.Height > 0 && inner.Contains(point));
        }

        var radiusX = Math.Max(Box.Width / 2, 1);
        var radiusY = Math.Max(Box.Height / 2, 1);
        var normalisedX = (point.X - Box.MidX) / radiusX;
        var normalisedY = (point.Y - Box.MidY) / radiusY;
        var distance = (float)Math.Sqrt((normalisedX * normalisedX) + (normalisedY * normalisedY));
        return Math.Abs(distance - 1) * Math.Min(radiusX, radiusY) <= reach;
    }

    public override MarkupElement Transformed(SKMatrix matrix)
    {
        var corner = matrix.MapPoint(Box.Left, Box.Top);
        var opposite = matrix.MapPoint(Box.Right, Box.Bottom);
        return this with
        {
            Box = new SKRect(
                Math.Min(corner.X, opposite.X),
                Math.Min(corner.Y, opposite.Y),
                Math.Max(corner.X, opposite.X),
                Math.Max(corner.Y, opposite.Y)),
            Style = ScaledStyle(matrix),
        };
    }
}

/// <summary>Text, positioned by the top-left of its first line and rotated around that point.</summary>
public sealed record TextElement : MarkupElement
{
    private static readonly SKTypeface Typeface = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default;

    public required SKPoint Position { get; init; }

    public required string Text { get; init; }

    public required float FontSize { get; init; }

    public float Rotation { get; init; }

    public override SKRect Bounds
    {
        get
        {
            var size = MeasuredSize();
            var matrix = SKMatrix.CreateRotationDegrees(Rotation, Position.X, Position.Y);
            return matrix.MapRect(new SKRect(Position.X, Position.Y, Position.X + size.Width, Position.Y + size.Height));
        }
    }

    public override void Draw(SKCanvas canvas)
    {
        using var font = CreateFont();
        using var paint = new SKPaint { Color = Style.EffectiveColor, IsAntialias = true };
        var metrics = font.Metrics;
        var lineHeight = font.Spacing;

        canvas.Save();
        canvas.RotateDegrees(Rotation, Position.X, Position.Y);

        var baseline = Position.Y - metrics.Ascent;
        foreach (var line in Lines)
        {
            canvas.DrawText(line, Position.X, baseline, SKTextAlign.Left, font, paint);
            baseline += lineHeight;
        }

        canvas.Restore();
    }

    public override bool HitTest(SKPoint point, float tolerance)
    {
        var inverse = SKMatrix.CreateRotationDegrees(-Rotation, Position.X, Position.Y);
        var local = inverse.MapPoint(point);
        var size = MeasuredSize();
        var box = new SKRect(Position.X, Position.Y, Position.X + size.Width, Position.Y + size.Height);
        return SKRect.Inflate(box, tolerance, tolerance).Contains(local);
    }

    public override MarkupElement Transformed(SKMatrix matrix)
    {
        var scale = UniformScale(matrix);
        var determinant = (matrix.ScaleX * matrix.ScaleY) - (matrix.SkewX * matrix.SkewY);

        if (determinant < 0)
        {
            // A mirrored image shouldn't mean mirrored writing: move the text, keep it readable.
            var mapped = matrix.MapRect(Bounds);
            return this with
            {
                Position = new SKPoint(mapped.Left, mapped.Top),
                FontSize = FontSize * scale,
                Style = ScaledStyle(matrix),
            };
        }

        var turn = (float)(Math.Atan2(matrix.SkewY, matrix.ScaleX) * 180 / Math.PI);
        return this with
        {
            Position = matrix.MapPoint(Position),
            FontSize = FontSize * scale,
            Rotation = Rotation + turn,
            Style = ScaledStyle(matrix),
        };
    }

    private string[] Lines => Text.Replace("\r\n", "\n").Split('\n');

    private SKFont CreateFont() => new(Typeface, FontSize) { Subpixel = true, Edging = SKFontEdging.Antialias };

    private SKSize MeasuredSize()
    {
        using var font = CreateFont();
        var width = Lines.Max(line => font.MeasureText(line));
        return new SKSize(width, font.Spacing * Lines.Length);
    }
}
