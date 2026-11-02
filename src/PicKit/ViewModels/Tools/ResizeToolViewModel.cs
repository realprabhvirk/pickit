using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Controls;
using PicKit.Controls.Tools;
using PicKit.Core.Files;
using PicKit.Core.Imaging;
using PicKit.Services;
using SkiaSharp;

namespace PicKit.ViewModels.Tools;

public sealed record UnitChoice(SizeUnit Unit, string Label);

public sealed partial class ResizeToolViewModel : ToolViewModel
{
    private readonly CropCanvasTool _cropGuide = new();
    private readonly SizeEstimator _estimator = new();
    private readonly DocumentSaver _saver;
    private bool _isSyncing;
    private int _pixelWidth = 1;
    private int _pixelHeight = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasTool))]
    [NotifyPropertyChangedFor(nameof(ShowsKeepAspect))]
    [NotifyPropertyChangedFor(nameof(ModeDescription))]
    private ResizeMode _mode = ResizeMode.Scale;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsDpi))]
    [NotifyPropertyChangedFor(nameof(WidthLabel))]
    [NotifyPropertyChangedFor(nameof(HeightLabel))]
    private UnitChoice _selectedUnit;

    [ObservableProperty]
    private string _dpiText = PhysicalSize.DefaultDpi.ToString();

    [ObservableProperty]
    private string _printText = string.Empty;

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
        _selectedUnit = Units[0];
    }

    public IReadOnlyList<UnitChoice> Units { get; } =
    [
        new(SizeUnit.Pixels, "Pixels"),
        new(SizeUnit.Millimetres, "mm"),
        new(SizeUnit.Centimetres, "cm"),
    ];

    public override string Name => "Resize";

    public override string Glyph => "";

    public override ICanvasTool? CanvasTool => Mode == ResizeMode.CropToFill ? _cropGuide : null;

    private SizeUnit Unit => SelectedUnit.Unit;

    private string UnitName => Unit == SizeUnit.Pixels ? "pixels" : SelectedUnit.Label;

    private bool HasValidDpi => int.TryParse(DpiText, out var dpi) && PhysicalSize.IsValidDpi(dpi);

    private int Dpi => HasValidDpi ? int.Parse(DpiText) : PhysicalSize.DefaultDpi;

    public bool ShowsKeepAspect => Mode == ResizeMode.Scale;

    /// <summary>A print size means nothing without a resolution, so it's only asked for then.</summary>
    public bool ShowsDpi => Unit != SizeUnit.Pixels;

    public string WidthLabel => $"Width ({UnitName})";

    public string HeightLabel => $"Height ({UnitName})";

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

    partial void OnSelectedUnitChanged(UnitChoice value)
    {
        ShowPixels(_pixelWidth, _pixelHeight);
        Recalculate();
    }

    partial void OnDpiTextChanged(string value) => Recalculate();

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

        ShowPixels(pixels.Width, pixels.Height);
        OriginalText = $"{pixels.Width} × {pixels.Height}";
        Recalculate();
    }

    private void ShowPixels(int width, int height)
    {
        _pixelWidth = width;
        _pixelHeight = height;
        _isSyncing = true;
        WidthText = Display(width);
        HeightText = Display(height);
        _isSyncing = false;
    }

    private string Display(int pixels) => PhysicalSize.Format(PhysicalSize.FromPixels(pixels, Unit, Dpi), Unit);

    private void SyncFromWidth()
    {
        if (KeepAspect && Mode == ResizeMode.Scale && Editor.Document?.Pixels is { } pixels && TryParse(WidthText, out var width))
        {
            _isSyncing = true;
            HeightText = Display(ResizeCalculator.HeightForWidth(pixels.Width, pixels.Height, width));
            _isSyncing = false;
        }
    }

    private void SyncFromHeight()
    {
        if (KeepAspect && Mode == ResizeMode.Scale && Editor.Document?.Pixels is { } pixels && TryParse(HeightText, out var height))
        {
            _isSyncing = true;
            WidthText = Display(ResizeCalculator.WidthForHeight(pixels.Width, pixels.Height, height));
            _isSyncing = false;
        }
    }

    private void Recalculate()
    {
        Warning = null;
        ResultText = string.Empty;
        PrintText = string.Empty;
        EstimateText = string.Empty;
        _estimator.Cancel();

        if (Editor.Document?.Pixels is not { } pixels)
        {
            IsValid = false;
            return;
        }

        if (ShowsDpi && !HasValidDpi)
        {
            Warning = $"Enter a print resolution from 1 to {PhysicalSize.MaxDpi} DPI.";
            IsValid = false;
            return;
        }

        if (!TryParse(WidthText, out var width) || !TryParse(HeightText, out var height))
        {
            Warning = ShowsDpi
                ? $"That size has to come to between 1 and {ImageLimits.MaxDimension} pixels."
                : $"Enter a width and height from 1 to {ImageLimits.MaxDimension}.";
            IsValid = false;
            return;
        }

        _pixelWidth = width;
        _pixelHeight = height;
        var plan = ResizeCalculator.Plan(pixels.Width, pixels.Height, width, height, Mode);
        ResultText = $"{plan.Width} × {plan.Height}";
        PrintText = DescribePrintSize(plan);
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

    private string DescribePrintSize(ResizePlan plan)
    {
        if (!ShowsDpi)
        {
            return string.Empty;
        }

        var size = $"{Display(plan.Width)} × {Display(plan.Height)} {UnitName} at {Dpi} DPI";
        return Editor.Document is { Source.Format: var format } && ImageResolution.CanStore(format)
            ? $"Prints at {size}."
            : $"Sized for {size}. This file type can't store a print size, so save as JPEG or PNG to keep it.";
    }

    /// <summary>Reads a typed size as pixels. Whole pixels only, unless it's a print size.</summary>
    private bool TryParse(string text, out int pixels)
    {
        pixels = 0;
        if (!PhysicalSize.TryParse(text, out var value) || (Unit == SizeUnit.Pixels && value != Math.Floor(value)))
        {
            return false;
        }

        pixels = PhysicalSize.ToPixels(value, Unit, Dpi);
        return pixels is > 0 and <= ImageLimits.MaxDimension;
    }

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

        var document = Editor.Document;
        var dpi = ShowsDpi ? Dpi : (int?)null;
        var mapping = MarkupMapping(pixels.Width, pixels.Height, plan);
        await Editor.ApplyAsync("Resize", source => ImageOps.ApplyPlan(source, plan), () => mapping);
        document!.PrintDpi = dpi;
    }

    private static SKMatrix MarkupMapping(int sourceWidth, int sourceHeight, ResizePlan plan)
    {
        var crop = plan.Crop ?? new SKRectI(0, 0, sourceWidth, sourceHeight);
        var scale = SKMatrix.CreateScale((float)plan.Width / crop.Width, (float)plan.Height / crop.Height);
        return SKMatrix.CreateTranslation(-crop.Left, -crop.Top).PostConcat(scale);
    }
}
