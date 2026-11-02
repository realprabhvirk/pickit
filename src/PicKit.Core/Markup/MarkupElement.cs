using SkiaSharp;

namespace PicKit.Core.Markup;

/// <summary>
/// A vector annotation in image coordinates. Elements are immutable: an edit makes a new one,
/// so undo only has to remember which list was current.
/// </summary>
public abstract record MarkupElement
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public abstract SKRect Bounds { get; }

    public abstract void Draw(SKCanvas canvas);

    /// <summary>True if a point lies on the element, within a tolerance in image pixels.</summary>
    public abstract bool HitTest(SKPoint point, float tolerance);

    /// <summary>The same element after the image underneath it was cropped, scaled or rotated.</summary>
    public abstract MarkupElement Transformed(SKMatrix matrix);
}
