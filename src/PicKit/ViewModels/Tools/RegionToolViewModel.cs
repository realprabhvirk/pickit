using CommunityToolkit.Mvvm.ComponentModel;
using PicKit.Controls;
using PicKit.Controls.Tools;
using PicKit.Core.Imaging;
using SkiaSharp;

namespace PicKit.ViewModels.Tools;

public sealed record RegionShapeChoice(RegionShape Shape, string Label);

/// <summary>
/// An effect applied to an area as soon as it's marked, undone with Undo. A dragged box is the
/// default; rounded boxes, circles and a brush are there when an area isn't rectangular.
/// </summary>
public abstract partial class RegionEffectToolViewModel : ToolViewModel
{
    private const float ReferenceSide = 1000;

    private readonly RegionCanvasTool _canvasTool;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsBrushSize))]
    [NotifyPropertyChangedFor(nameof(ShapeHint))]
    private RegionShapeChoice _selectedShape;

    [ObservableProperty]
    private double _brushSize = 40;

    protected RegionEffectToolViewModel(EditorViewModel editor)
        : base(editor)
    {
        _selectedShape = Shapes[0];
        _canvasTool = new RegionCanvasTool(box => _ = ApplyToBoxAsync(box))
        {
            OutlineDrawn = outline => _ = ApplyToOutlineAsync(outline),
        };
    }

    public IReadOnlyList<RegionShapeChoice> Shapes { get; } =
    [
        new(RegionShape.Box, "Box"),
        new(RegionShape.Rounded, "Rounded"),
        new(RegionShape.Ellipse, "Circle"),
        new(RegionShape.Brush, "Brush"),
    ];

    public override ICanvasTool CanvasTool => _canvasTool;

    public bool ShowsBrushSize => SelectedShape.Shape == RegionShape.Brush;

    public string ShapeHint => SelectedShape.Shape == RegionShape.Brush
        ? $"Paint over the area to {Verb}."
        : $"Drag over the area to {Verb}.";

    /// <summary>Sizes are relative to the image, so a setting looks the same on any photo.</summary>
    protected float ImageUnit =>
        Editor.Document?.Pixels is { } pixels ? Math.Max(1, Math.Max(pixels.Width, pixels.Height) / ReferenceSide) : 1;

    protected abstract string Verb { get; }

    public override void Activate() => SyncBrush();

    public override void OnImageChanged() => SyncBrush();

    /// <summary>Returns the effect's new pixels for a whole region.</summary>
    protected abstract ImageBuffer Replace(ImageBuffer source, SKRectI region);

    partial void OnSelectedShapeChanged(RegionShapeChoice value)
    {
        _canvasTool.Shape = value.Shape;
        Editor.RedrawCanvas();
    }

    partial void OnBrushSizeChanged(double value) => SyncBrush();

    private void SyncBrush()
    {
        _canvasTool.BrushWidth = (float)BrushSize * ImageUnit;
        Editor.RedrawCanvas();
    }

    private Task ApplyToBoxAsync(SKRect box) => Editor.ApplyRegionAsync(Name, [box], Replace);

    private async Task ApplyToOutlineAsync(RegionOutline outline)
    {
        using (outline)
        {
            await Editor.ApplyRegionAsync(Name, [outline.Bounds], (source, region) =>
            {
                using var replaced = Replace(source, region);
                return ImageOps.ThroughOutline(source, region, replaced, outline);
            });
        }
    }
}

public sealed partial class BlurToolViewModel : RegionEffectToolViewModel
{
    [ObservableProperty]
    private double _strength = 12;

    public BlurToolViewModel(EditorViewModel editor)
        : base(editor)
    {
    }

    public override string Name => "Blur";

    public override string Glyph => "";

    protected override string Verb => "blur";

    protected override ImageBuffer Replace(ImageBuffer source, SKRectI region) =>
        ImageOps.BlurRegion(source, region, (float)Strength * ImageUnit);
}

public sealed partial class PixelateToolViewModel : RegionEffectToolViewModel
{
    [ObservableProperty]
    private double _blockSize = 16;

    public PixelateToolViewModel(EditorViewModel editor)
        : base(editor)
    {
    }

    public override string Name => "Pixelate";

    public override string Glyph => "";

    protected override string Verb => "pixelate";

    protected override ImageBuffer Replace(ImageBuffer source, SKRectI region) =>
        ImageOps.PixelateRegion(source, region, (int)Math.Round(BlockSize * ImageUnit));
}
