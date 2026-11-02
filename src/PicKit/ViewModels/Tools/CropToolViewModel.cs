using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Controls;
using PicKit.Controls.Tools;
using PicKit.Core.Imaging;
using SkiaSharp;

namespace PicKit.ViewModels.Tools;

public sealed record AspectChoice(string Label, float? Ratio, bool IsCustom = false);

public sealed partial class CropToolViewModel : ToolViewModel
{
    private readonly CropCanvasTool _canvasTool = new();

    [ObservableProperty]
    private AspectChoice _selectedAspect;

    [ObservableProperty]
    private string _customWidth = "5";

    [ObservableProperty]
    private string _customHeight = "4";

    [ObservableProperty]
    private string _sizeText = string.Empty;

    public CropToolViewModel(EditorViewModel editor)
        : base(editor)
    {
        SelectedAspect = Aspects[0];
        _canvasTool.BoxChanged += OnBoxChanged;
    }

    public IReadOnlyList<AspectChoice> Aspects { get; } =
    [
        new("Free", null),
        new("1:1", 1f),
        new("4:3", 4f / 3f),
        new("3:2", 3f / 2f),
        new("16:9", 16f / 9f),
        new("9:16", 9f / 16f),
        new("Custom", null, IsCustom: true),
    ];

    public override string Name => "Crop";

    public override string Glyph => "";

    public override ICanvasTool CanvasTool => _canvasTool;

    public bool IsCustomAspect => SelectedAspect.IsCustom;

    public override void Activate() => ResetBox();

    public override void OnImageChanged() => ResetBox();

    public override bool ApplyPending()
    {
        _ = ApplyCommand.ExecuteAsync(null);
        return true;
    }

    partial void OnSelectedAspectChanged(AspectChoice value)
    {
        OnPropertyChanged(nameof(IsCustomAspect));
        _canvasTool.SetAspect(CurrentRatio());
    }

    partial void OnCustomWidthChanged(string value) => ApplyCustomAspect();

    partial void OnCustomHeightChanged(string value) => ApplyCustomAspect();

    private void ApplyCustomAspect()
    {
        if (IsCustomAspect)
        {
            _canvasTool.SetAspect(CurrentRatio());
        }
    }

    private float? CurrentRatio()
    {
        if (!SelectedAspect.IsCustom)
        {
            return SelectedAspect.Ratio;
        }

        var valid = float.TryParse(CustomWidth, out var width) && float.TryParse(CustomHeight, out var height)
            && width > 0 && height > 0;

        return valid ? float.Parse(CustomWidth) / float.Parse(CustomHeight) : null;
    }

    private void ResetBox()
    {
        if (Editor.Document is { } document)
        {
            _canvasTool.Reset(document.Pixels.Width, document.Pixels.Height, CurrentRatio());
        }
    }

    private void OnBoxChanged()
    {
        var box = _canvasTool.PixelBox;
        SizeText = $"{box.Width} × {box.Height}";
        Editor.RedrawCanvas();
    }

    [RelayCommand]
    private void SwapOrientation()
    {
        if (CurrentRatio() is { } ratio)
        {
            var match = Aspects.FirstOrDefault(choice => choice.Ratio is { } other && Math.Abs(other - (1 / ratio)) < 0.001f);
            if (match is not null)
            {
                SelectedAspect = match;
            }
            else
            {
                (CustomWidth, CustomHeight) = (CustomHeight, CustomWidth);
            }
        }
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var box = _canvasTool.PixelBox;
        await Editor.ApplyAsync(
            "Crop",
            pixels => ImageOps.Crop(pixels, box),
            () => SKMatrix.CreateTranslation(-box.Left, -box.Top));

        Editor.ClearActiveTool();
    }

    [RelayCommand]
    private void Cancel() => Editor.ClearActiveTool();
}
