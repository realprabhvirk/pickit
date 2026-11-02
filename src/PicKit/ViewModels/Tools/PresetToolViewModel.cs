using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Controls;
using PicKit.Controls.Tools;
using PicKit.Core.Imaging;
using PicKit.Core.Presets;
using SkiaSharp;

namespace PicKit.ViewModels.Tools;

/// <summary>"Resize for": pick a destination, see what would be cut, then choose. Nothing is cut silently.</summary>
public sealed partial class PresetToolViewModel : ToolViewModel
{
    private readonly CropCanvasTool _cropGuide = new();
    private IReadOnlyList<PresetGroup>? _groups;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasTool))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(CanCrop))]
    [NotifyPropertyChangedFor(nameof(CropSummary))]
    private PlatformPreset? _selectedPreset;

    public PresetToolViewModel(EditorViewModel editor)
        : base(editor)
    {
        _cropGuide.BoxChanged += Editor.RedrawCanvas;
    }

    public IReadOnlyList<PresetGroup> Groups => _groups ??= PresetCatalog.Load().Groups;

    /// <summary>A single grouped list, so only one destination can ever be selected. Built on first use.</summary>
    public ICollectionView? Destinations { get; private set; }

    public override string Name => "Resize for";

    public override string Glyph => "";

    public override ICanvasTool? CanvasTool => CanCrop ? _cropGuide : null;

    public bool HasSelection => SelectedPreset is not null;

    /// <summary>Whether this preset has a fixed shape the image could be cropped to.</summary>
    public bool CanCrop => SelectedPreset is { FitOnly: false };

    public string CropSummary
    {
        get
        {
            if (SelectedPreset is not { } preset || Editor.Document?.Pixels is not { } pixels)
            {
                return string.Empty;
            }

            if (preset.FitOnly)
            {
                return "No cropping needed.";
            }

            var plan = ResizeCalculator.Plan(pixels.Width, pixels.Height, preset.Width, preset.Height, ResizeMode.CropToFill);
            return plan.RequiresCrop
                ? "Your image is a different shape, so it won't fill this size without cropping. The dimmed area is what would be removed."
                : "Your image is already this shape. No cropping needed.";
        }
    }

    public override void Activate()
    {
        SelectedPreset = null;
        if (Destinations is null)
        {
            var destinations = new ListCollectionView(Groups.SelectMany(group => group.Presets).ToList());
            destinations.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PlatformPreset.Group)));
            Destinations = destinations;
            OnPropertyChanged(nameof(Destinations));
        }
    }

    public override void OnImageChanged() => ResetGuide();

    partial void OnSelectedPresetChanged(PlatformPreset? value)
    {
        ResetGuide();
        Editor.RefreshCanvasTool();
    }

    private void ResetGuide()
    {
        if (SelectedPreset is { FitOnly: false } preset && Editor.Document?.Pixels is { } pixels)
        {
            _cropGuide.Reset(pixels.Width, pixels.Height, (float)preset.Width / preset.Height);
        }

        OnPropertyChanged(nameof(CropSummary));
    }

    private Task Resize(PlatformPreset preset, ResizePlan plan)
    {
        var pixels = Editor.Document?.Pixels;
        if (pixels is null)
        {
            return Task.CompletedTask;
        }

        var crop = plan.Crop ?? new SKRectI(0, 0, pixels.Width, pixels.Height);
        var scale = SKMatrix.CreateScale((float)plan.Width / crop.Width, (float)plan.Height / crop.Height);
        var mapping = SKMatrix.CreateTranslation(-crop.Left, -crop.Top).PostConcat(scale);

        return Editor.ApplyAsync($"Resize for {preset.Name}", source => ImageOps.ApplyPlan(source, plan), () => mapping);
    }

    [RelayCommand]
    private async Task CropToFitAsync()
    {
        if (SelectedPreset is { } preset)
        {
            await Resize(preset, new ResizePlan(preset.Width, preset.Height, _cropGuide.PixelBox));
            SelectedPreset = null;
        }
    }

    [RelayCommand]
    private async Task FitInsideAsync()
    {
        if (SelectedPreset is { } preset && Editor.Document?.Pixels is { } pixels)
        {
            await Resize(preset, ResizeCalculator.Plan(pixels.Width, pixels.Height, preset.Width, preset.Height, ResizeMode.Fit));
            SelectedPreset = null;
        }
    }
}
