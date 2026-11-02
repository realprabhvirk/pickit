using System.Collections.Immutable;
using SkiaSharp;

namespace PicKit.Core.Markup;

/// <summary>
/// Rubs out markup along a pointer's path. Pen and highlighter strokes are vector lines, so
/// they're cut where the eraser passes and what's left stays. Shapes, lines and text can't be
/// partly removed, so touching one removes it. Undo brings everything back.
/// </summary>
public static class MarkupEraser
{
    public static ImmutableArray<MarkupElement> Erase(ImmutableArray<MarkupElement> elements, SKPoint from, SKPoint to, float radius)
    {
        var reach = SKRect.Inflate(new SKRect(Math.Min(from.X, to.X), Math.Min(from.Y, to.Y), Math.Max(from.X, to.X), Math.Max(from.Y, to.Y)), radius, radius);

        var result = ImmutableArray.CreateBuilder<MarkupElement>(elements.Length);
        var hasChanged = false;
        foreach (var element in elements)
        {
            if (!element.Bounds.IntersectsWith(reach))
            {
                result.Add(element);
            }
            else if (element is StrokeElement stroke)
            {
                var pieces = Cut(stroke, from, to, radius);
                hasChanged |= pieces.Count != 1 || !ReferenceEquals(pieces[0], stroke);
                result.AddRange(pieces);
            }
            else if (Touches(element, from, to, radius))
            {
                hasChanged = true;
            }
            else
            {
                result.Add(element);
            }
        }

        return hasChanged ? result.ToImmutable() : elements;
    }

    private static bool Touches(MarkupElement element, SKPoint from, SKPoint to, float radius)
    {
        var steps = Math.Max(1, (int)Math.Ceiling(SKPoint.Distance(from, to) / Math.Max(radius / 2, 1)));
        for (var step = 0; step <= steps; step++)
        {
            var share = step / (float)steps;
            var point = new SKPoint(from.X + ((to.X - from.X) * share), from.Y + ((to.Y - from.Y) * share));
            if (element.HitTest(point, radius))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the stroke itself if it wasn't touched, otherwise what's left of it.</summary>
    private static List<MarkupElement> Cut(StrokeElement stroke, SKPoint from, SKPoint to, float radius)
    {
        // Half the thickness, so the eraser clears the visible line and not just its centre.
        var reach = radius + (stroke.Style.Thickness / 2);
        var points = Densify(stroke.Points, Math.Max(radius / 2, 1));

        var pieces = new List<MarkupElement>();
        var run = ImmutableArray.CreateBuilder<SKPoint>();
        var wasCut = false;

        foreach (var point in points)
        {
            if (MarkupElement.DistanceToSegment(point, from, to) <= reach)
            {
                wasCut = true;
                AddPiece(pieces, stroke, run);
            }
            else
            {
                run.Add(point);
            }
        }

        if (!wasCut)
        {
            return [stroke];
        }

        AddPiece(pieces, stroke, run);
        return pieces;
    }

    private static void AddPiece(List<MarkupElement> pieces, StrokeElement stroke, ImmutableArray<SKPoint>.Builder run)
    {
        // A lone point left at the edge of a cut would show as a stray dot.
        if (run.Count >= 2)
        {
            pieces.Add(stroke with { Id = Guid.NewGuid(), Points = run.ToImmutable() });
        }

        run.Clear();
    }

    /// <summary>A fast stroke has long gaps between samples, and the eraser mustn't fall through them.</summary>
    private static List<SKPoint> Densify(ImmutableArray<SKPoint> points, float spacing)
    {
        var result = new List<SKPoint>(points.Length);
        for (var i = 0; i < points.Length; i++)
        {
            result.Add(points[i]);
            if (i + 1 == points.Length)
            {
                break;
            }

            var next = points[i + 1];
            var extra = (int)(SKPoint.Distance(points[i], next) / spacing);
            for (var step = 1; step <= extra; step++)
            {
                var share = step / (float)(extra + 1);
                result.Add(new SKPoint(points[i].X + ((next.X - points[i].X) * share), points[i].Y + ((next.Y - points[i].Y) * share)));
            }
        }

        return result;
    }
}
