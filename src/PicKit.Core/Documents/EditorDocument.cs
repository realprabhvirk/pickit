using System.Collections.Immutable;
using PicKit.Core.Imaging;
using PicKit.Core.Markup;
using SkiaSharp;

namespace PicKit.Core.Documents;

/// <summary>Where a document came from. A clipboard image has no path.</summary>
public sealed record DocumentSource(string? Path, ImageFormat Format, long FileSize, int FrameCount = 1)
{
    public string DisplayName => Path is null ? "Pasted image" : System.IO.Path.GetFileName(Path);
}

public enum DocumentChangeKind
{
    /// <summary>Every pixel may differ, and the image may be a different size.</summary>
    Replaced,

    /// <summary>Only the pixels in the given region changed.</summary>
    Region,

    /// <summary>Pixels are untouched; only the markup changed.</summary>
    Markup,
}

public readonly record struct DocumentChange(DocumentChangeKind Kind, SKRectI Region);

/// <summary>
/// The image being edited: pixels, markup over them, and the history of both. The file on disk
/// is never touched here; the original stays safe until the user explicitly saves.
/// </summary>
public sealed class EditorDocument : IDisposable
{
    private long _savedRevision;

    public EditorDocument(ImageBuffer pixels, DocumentSource source, EditHistory? history = null)
    {
        Pixels = pixels;
        Source = source;
        History = history ?? new EditHistory();
    }

    public event Action<DocumentChange>? Changed;

    public ImageBuffer Pixels { get; private set; }

    public ImmutableArray<MarkupElement> Markups { get; private set; } = [];

    public DocumentSource Source { get; private set; }

    public EditHistory History { get; }

    public bool IsDirty => History.Revision != _savedRevision;

    /// <summary>True once any edit has altered pixels, which rules out keeping old metadata.</summary>
    public bool HasPixelEdits { get; private set; }

    /// <summary>Replaces the whole image, moving markup along with the pixels.</summary>
    public void ReplacePixels(string label, ImageBuffer newPixels, SKMatrix? markupMapping = null)
    {
        var previousMarkups = Markups;
        if (markupMapping is { } mapping)
        {
            Markups = [.. Markups.Select(element => element.Transformed(mapping))];
        }

        History.Push(new ReplacePixelsStep(label, Pixels, previousMarkups));
        Pixels = newPixels;
        HasPixelEdits = true;
        Changed?.Invoke(new DocumentChange(DocumentChangeKind.Replaced, Pixels.Bounds));
    }

    /// <summary>Overwrites a rectangle. <paramref name="patch"/> must match the rectangle's size.</summary>
    public void PatchRegion(string label, SKRectI region, ImageBuffer patch)
    {
        if (patch.Width != region.Width || patch.Height != region.Height)
        {
            throw new ArgumentException("The patch doesn't match the region's size.", nameof(patch));
        }

        // The patch is exchanged with the image, leaving it holding the previous pixels for undo.
        PixelOps.SwapRegion(Pixels, patch, new SKPointI(region.Left, region.Top));
        History.Push(new PatchRegionStep(label, patch, new SKPointI(region.Left, region.Top)));
        HasPixelEdits = true;
        Changed?.Invoke(new DocumentChange(DocumentChangeKind.Region, region));
    }

    public void SetMarkups(string label, ImmutableArray<MarkupElement> markups)
    {
        History.Push(new MarkupStep(label, Markups));
        Markups = markups;
        Changed?.Invoke(new DocumentChange(DocumentChangeKind.Markup, Pixels.Bounds));
    }

    public void Undo() => History.Undo(this);

    public void Redo() => History.Redo(this);

    public void MarkSaved(DocumentSource source)
    {
        Source = source;
        _savedRevision = History.Revision;
    }

    /// <summary>The picture as exported: pixels with all markup burned in. The caller owns the result.</summary>
    public ImageBuffer RenderFlattened()
    {
        var result = Pixels.Clone();
        if (Markups.Length > 0)
        {
            using var canvas = result.CreateCanvas();
            MarkupRenderer.Draw(canvas, Markups);
        }

        return result;
    }

    public void Dispose()
    {
        History.Dispose();
        Pixels.Dispose();
    }

    internal (ImageBuffer Pixels, ImmutableArray<MarkupElement> Markups) ExchangeState(
        ImageBuffer pixels, ImmutableArray<MarkupElement> markups)
    {
        var previous = (Pixels, Markups);
        Pixels = pixels;
        Markups = markups;
        Changed?.Invoke(new DocumentChange(DocumentChangeKind.Replaced, Pixels.Bounds));
        return previous;
    }

    internal void ExchangeRegion(ImageBuffer patch, SKPointI origin)
    {
        PixelOps.SwapRegion(Pixels, patch, origin);
        var region = new SKRectI(origin.X, origin.Y, origin.X + patch.Width, origin.Y + patch.Height);
        Changed?.Invoke(new DocumentChange(DocumentChangeKind.Region, region));
    }

    internal ImmutableArray<MarkupElement> ExchangeMarkups(ImmutableArray<MarkupElement> markups)
    {
        var previous = Markups;
        Markups = markups;
        Changed?.Invoke(new DocumentChange(DocumentChangeKind.Markup, Pixels.Bounds));
        return previous;
    }
}
