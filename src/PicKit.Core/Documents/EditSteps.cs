using System.Collections.Immutable;
using PicKit.Core.Imaging;
using PicKit.Core.Markup;
using SkiaSharp;

namespace PicKit.Core.Documents;

/// <summary>Remembers a whole image, for edits that replace every pixel or change the size.</summary>
internal sealed class ReplacePixelsStep : EditStep
{
    private readonly PixelSlot _pixels;
    private ImmutableArray<MarkupElement> _markups;

    public ReplacePixelsStep(string label, ImageBuffer previousPixels, ImmutableArray<MarkupElement> previousMarkups)
        : base(label)
    {
        _pixels = new PixelSlot(previousPixels);
        _markups = previousMarkups;
    }

    public override long BytesInMemory => _pixels.BytesInMemory;

    public override void Exchange(EditorDocument document)
    {
        var incoming = _pixels.Take();
        var (outgoing, outgoingMarkups) = document.ExchangeState(incoming, _markups);
        _pixels.Store(outgoing);
        _markups = outgoingMarkups;
    }

    public override void MoveToDisk() => _pixels.MoveToDisk();

    public override void Dispose() => _pixels.Dispose();
}

/// <summary>Remembers only the rectangle that changed, so a small blur costs a small undo.</summary>
internal sealed class PatchRegionStep : EditStep
{
    private readonly PixelSlot _patch;
    private readonly SKPointI _origin;

    public PatchRegionStep(string label, ImageBuffer previousRegion, SKPointI origin)
        : base(label)
    {
        _patch = new PixelSlot(previousRegion);
        _origin = origin;
    }

    public override long BytesInMemory => _patch.BytesInMemory;

    public override void Exchange(EditorDocument document)
    {
        var patch = _patch.Take();
        document.ExchangeRegion(patch, _origin);
        _patch.Store(patch);
    }

    public override void MoveToDisk() => _patch.MoveToDisk();

    public override void Dispose() => _patch.Dispose();
}

internal sealed class MarkupStep : EditStep
{
    private ImmutableArray<MarkupElement> _markups;

    public MarkupStep(string label, ImmutableArray<MarkupElement> previousMarkups)
        : base(label)
    {
        _markups = previousMarkups;
    }

    public override void Exchange(EditorDocument document) => _markups = document.ExchangeMarkups(_markups);
}
