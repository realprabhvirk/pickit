using SkiaSharp;

namespace PicKit.Core.Markup;

public static class MarkupRenderer
{
    /// <summary>Draws in image coordinates. Used for both the screen and exported files.</summary>
    public static void Draw(SKCanvas canvas, IEnumerable<MarkupElement> elements)
    {
        foreach (var element in elements)
        {
            element.Draw(canvas);
        }
    }
}
