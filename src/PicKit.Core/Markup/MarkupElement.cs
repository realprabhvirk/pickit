using SkiaSharp;

namespace PicKit.Core.Markup;

/// <summary>Appearance shared by every kind of markup.</summary>
public sealed record MarkupStyle(SKColor Color, float Thickness, float Opacity = 1)
{
    public SKColor EffectiveColor => Color.WithAlpha((byte)Math.Clamp(Math.Round(Opacity * 255), 0, 255));
}

/// <summary>
/// A vector annotation in image coordinates. Elements are immutable: an edit makes a new one,
/// so undo only has to remember which list was current.
/// </summary>
public abstract record MarkupElement
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public required MarkupStyle Style { get; init; }

    public abstract SKRect Bounds { get; }

    public abstract void Draw(SKCanvas canvas);

    /// <summary>True if a point lies on the element, within a tolerance in image pixels.</summary>
    public abstract bool HitTest(SKPoint point, float tolerance);

    /// <summary>The same element after the image underneath it was cropped, scaled or rotated.</summary>
    public abstract MarkupElement Transformed(SKMatrix matrix);

    public MarkupElement Moved(float deltaX, float deltaY) => Transformed(SKMatrix.CreateTranslation(deltaX, deltaY));

    public MarkupElement Restyled(MarkupStyle style) => this with { Style = style };

    /// <summary>How much a matrix changes lengths, so strokes keep their look through a resize.</summary>
    protected static float UniformScale(SKMatrix matrix) =>
        (float)Math.Sqrt(Math.Abs((matrix.ScaleX * matrix.ScaleY) - (matrix.SkewX * matrix.SkewY)));

    protected MarkupStyle ScaledStyle(SKMatrix matrix) =>
        Style with { Thickness = Style.Thickness * UniformScale(matrix) };

    internal static float DistanceToSegment(SKPoint point, SKPoint start, SKPoint end)
    {
        var lengthSquared = SKPoint.DistanceSquared(start, end);
        if (lengthSquared <= float.Epsilon)
        {
            return SKPoint.Distance(point, start);
        }

        var along = (((point.X - start.X) * (end.X - start.X)) + ((point.Y - start.Y) * (end.Y - start.Y))) / lengthSquared;
        along = Math.Clamp(along, 0, 1);
        var nearest = new SKPoint(start.X + (along * (end.X - start.X)), start.Y + (along * (end.Y - start.Y)));
        return SKPoint.Distance(point, nearest);
    }

    protected SKPaint StrokePaint(SKStrokeCap cap = SKStrokeCap.Round) => new()
    {
        Color = Style.EffectiveColor,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = Style.Thickness,
        StrokeCap = cap,
        StrokeJoin = SKStrokeJoin.Round,
        IsAntialias = true,
    };
}
