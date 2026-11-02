using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Controls;
using PicKit.Controls.Tools;
using PicKit.Core.Files;
using PicKit.Core.Imaging;
using PicKit.Services;
using SkiaSharp;

namespace PicKit.ViewModels.Tools;

public sealed partial class ResizeToolViewModel : ToolViewModel
{
    private readonly CropCanvasTool _cropGuide = new();
    private readonly SizeEstimator _estimator = new();
    private readonly DocumentSaver _saver;
    private bool _isSyncing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasTool))]
    [NotifyPropertyChangedFor(nameof(ShowsKeepAspect))]
    [NotifyPropertyChangedFor(nameof(ModeDescription))]
    private ResizeMode _mode = ResizeMode.Scale;

    [ObservableProperty]
    private string _widthText = string.Empty;

    [ObservableProperty]
    private string _heightText = string.Empty;

    [ObservableProperty]
    private bool _keepAspect = true;

    [ObservableProperty]
    private string _originalText = string.Empty;

    [ObservableProperty]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private string _estimateText = string.Empty;

    [ObservableProperty]
    private string? _warning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _isValid;

    public ResizeToolViewModel(EditorViewModel editor, DocumentSaver saver)
        : base(editor)
    {
        _saver = saver;
    }

    public override string Name => "Resize";

    public override string Glyph => "";

    public override ICanvasTool? CanvasTool => Mode == ResizeMode.CropToFill ? _cropGuide : null;

    public bool ShowsKeepAspect => Mode == ResizeMode.Scale;

    public string ModeDescription => Mode switch
    {
        ResizeMode.Scale => "Changes the dimensions. Nothing is cut off.",
        ResizeMode.Fit => "Shrinks to fit inside the box. Nothing is cut off.",
        _ => "Cuts to this shape, then resizes. Drag the box to choose what stays.",
    };

    public override void Activate() => LoadFromImage();

    public override void OnImageChanged() => LoadFromImage();

    public override void Deactivate() => _estimator.Cancel();

    public override bool ApplyPending()
    {
        if (ApplyCommand.CanExecute(null))
        {
            _ = ApplyCommand.ExecuteAsync(null);
        }

        return true;
    }

    partial void OnModeChanged(ResizeMode value)
    {
        Editor.RefreshCanvasTool();
        Recalculate();
    }

    partial void OnKeepAspectChanged(bool value)
    {
        if (value)
        {
            SyncFromWidth();
        }

        Recalculate();
    }

    partial void OnWidthTextChanged(string value)
    {
        if (!_isSyncing)
        {
            SyncFromWidth();
            Recalculate();
        }
    }

    partial void OnHeightTextChanged(string value)
    {
        if (!_isSyncing)
        {
            SyncFromHeight();
            Recalculate();
        }
    }

    private void LoadFromImage()
    {
        if (Editor.Document?.Pixels is not { } pixels)
        {
            return;
        }

        _isSyncing = true;
        WidthText = pixels.Width.ToString();
        HeightText = pixels.Height.ToString();
        _isSyncing = false;
        OriginalText = $"{pixels.Width} × {pixels.Height}";
        Recalculate();
    }

    private void SyncFromWidth()
    {
        if (KeepAspect && Mode == ResizeMode.Scale && Editor.Document?.Pixels is { } pixels && TryParse(WidthText, out var width))
        {
            _isSyncing = true;
            HeightText = ResizeCalculator.HeightForWidth(pixels.Width, pixels.Height, width).ToString();
            _isSyncing = false;
        }
    }

    private void SyncFromHeight()
    {
        if (KeepAspect && Mode == ResizeMode.Scale && Editor.Document?.Pixels is { } pixels && TryParse(HeightText, out var height))
        {
            _isSyncing = true;
            WidthText = ResizeCalculator.WidthForHeight(pixels.Width, pixels.Height, height).ToString();
            _isSyncing = false;
        }
    }

    private void Recalculate()
    {
        Warning = null;
        ResultText = string.Empty;
        EstimateText = string.Empty;
        _estimator.Cancel();

        if (Editor.Document?.Pixels is not { } pixels)
        {
            IsValid = false;
            return;
        }

        if (!TryParse(WidthText, out var width) || !TryParse(HeightText, out var height))
        {
            Warning = $"Enter a width and height from 1 to {ImageLimits.MaxDimension}.";
            IsValid = false;
            return;
        }

        var plan = ResizeCalculator.Plan(pixels.Width, pixels.Height, width, height, Mode);
        ResultText = $"{plan.Width} × {plan.Height}";
        IsValid = ImageLimits.Fits(plan.Width, plan.Height);

        if (!IsValid)
        {
            Warning = "That's too large to create on this PC.";
        }
        else if (Mode == ResizeMode.Scale && ResizeCalculator.IsStretched(pixels.Width, pixels.Height, width, height))
        {
            Warning = "These dimensions will stretch the image.";
        }
        else if (Mode == ResizeMode.Fit && plan.Width == pixels.Width && plan.Height == pixels.Height)
        {
            Warning = "The image already fits. Fit doesn't enlarge images.";
        }

        if (Mode == ResizeMode.CropToFill)
        {
            _cropGuide.Reset(pixels.Width, pixels.Height, (float)width / height);
        }

        if (IsValid)
        {
            EstimateSize(plan);
        }
    }

    private async void EstimateSize(ResizePlan plan)
    {
        if (Editor.Document is not { } document)
        {
            return;
        }

        var format = ImageFormats.CanEncode(document.Source.Format) ? document.Source.Format : ImageFormat.Png;
        var options = new EncodeOptions(format, _saver.QualityFor(format), SKColors.White);

        EstimateText = "Estimating...";
        var size = await _estimator.EstimateAsync(document.RenderFlattened(), flattened =>
        {
            using var resized = ImageOps.ApplyPlan(flattened, plan);
            return ImageEncoder.Encode(resized, options).LongLength;
        });

        if (size is { } estimated)
        {
            EstimateText = $"About {FileSizeText.Format(estimated)}";
        }
    }

    private static bool TryParse(string text, out int value) =>
        int.TryParse(text, out value) && value is > 0 and <= ImageLimits.MaxDimension;

    private bool CanApply() => IsValid;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (Editor.Document?.Pixels is not { } pixels
            || !TryParse(WidthText, out var width)
            || !TryParse(HeightText, out var height))
        {
            return;
        }

        var plan = ResizeCalculator.Plan(pixels.Width, pixels.Height, width, height, Mode);
        if (Mode == ResizeMode.CropToFill)
        {
            plan = plan with { Crop = _cropGuide.PixelBox };
        }

        var mapping = MarkupMapping(pixels.Width, pixels.Height, plan);
        await Editor.ApplyAsync("Resize", source => ImageOps.ApplyPlan(source, plan), () => mapping);
    }

    private static SKMatrix MarkupMapping(int sourceWidth, int sourceHeight, ResizePlan plan)
    {
        var crop = plan.Crop ?? new SKRectI(0, 0, sourceWidth, sourceHeight);
        var scale = SKMatrix.CreateScale((float)plan.Width / crop.Width, (float)plan.Height / crop.Height);
        return SKMatrix.CreateTranslation(-crop.Left, -crop.Top).PostConcat(scale);
    }
}
