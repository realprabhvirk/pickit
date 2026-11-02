using CommunityToolkit.Mvvm.ComponentModel;
using PicKit.Controls;
using PicKit.Controls.Tools;
using PicKit.Core.Documents;
using PicKit.Core.Imaging;
using SkiaSharp;

namespace PicKit.ViewModels.Tools;

/// <summary>Blur: drag over an area and it's blurred at once. Undo brings it back.</summary>
public sealed partial class BlurToolViewModel : ToolViewModel
{
    private const float ReferenceSide = 1000;

    private readonly RegionCanvasTool _canvasTool;

    [ObservableProperty]
    private double _strength = 12;

    public BlurToolViewModel(EditorViewModel editor)
        : base(editor)
    {
        _canvasTool = new RegionCanvasTool(box => _ = ApplyToRegionAsync(box));
    }

    public override string Name => "Blur";

    public override string Glyph => "";

    public override ICanvasTool CanvasTool => _canvasTool;

    private async Task ApplyToRegionAsync(SKRect box)
    {
        if (Editor.Document?.Pixels is { } pixels)
        {
            var sigma = (float)Strength * Math.Max(1, Math.Max(pixels.Width, pixels.Height) / ReferenceSide);
            await Editor.ApplyRegionAsync("Blur", [box], (source, region) => ImageOps.BlurRegion(source, region, sigma));
        }
    }
}

/// <summary>Pixelate: drag over an area and it turns to a mosaic at once. Undo brings it back.</summary>
public sealed partial class PixelateToolViewModel : ToolViewModel
{
    private const float ReferenceSide = 1000;

    private readonly RegionCanvasTool _canvasTool;

    [ObservableProperty]
    private double _blockSize = 16;

    public PixelateToolViewModel(EditorViewModel editor)
        : base(editor)
    {
        _canvasTool = new RegionCanvasTool(box => _ = ApplyToRegionAsync(box));
    }

    public override string Name => "Pixelate";

    public override string Glyph => "";

    public override ICanvasTool CanvasTool => _canvasTool;

    private async Task ApplyToRegionAsync(SKRect box)
    {
        if (Editor.Document?.Pixels is { } pixels)
        {
            var block = (int)Math.Round(BlockSize * Math.Max(1, Math.Max(pixels.Width, pixels.Height) / ReferenceSide));
            await Editor.ApplyRegionAsync("Pixelate", [box], (source, region) => ImageOps.PixelateRegion(source, region, block));
        }
    }
}
