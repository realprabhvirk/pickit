using System.Diagnostics;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Core.Diagnostics;
using PicKit.Core.Documents;
using PicKit.Core.Files;
using PicKit.Core.Imaging;
using PicKit.Services;
using SkiaSharp;

namespace PicKit.ViewModels.Tools;

public enum CompressMode
{
    Quality,
    TargetSize,
}

public sealed record FormatChoice(string Label, ImageFormat Format);

public sealed partial class CompressToolViewModel : ToolViewModel
{
    private readonly DocumentSaver _saver;
    private readonly DialogService _dialogs;
    private readonly SizeEstimator _estimator = new();
    private CancellationTokenSource? _running;
    private string? _lastOutput;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsQualityMode))]
    [NotifyPropertyChangedFor(nameof(IsTargetMode))]
    [NotifyCanExecuteChangedFor(nameof(CompressCommand))]
    private CompressMode _mode = CompressMode.Quality;

    [ObservableProperty]
    private CompressionPreset _preset = CompressionPreset.Balanced;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompressCommand))]
    private string _targetText = "2.0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormatNote))]
    [NotifyPropertyChangedFor(nameof(UsesQuality))]
    private FormatChoice _selectedFormat;

    [ObservableProperty]
    private string _originalText = string.Empty;

    [ObservableProperty]
    private string _estimateText = string.Empty;

    [ObservableProperty]
    private string? _resultText;

    [ObservableProperty]
    private string? _problemText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanShowResult))]
    [NotifyCanExecuteChangedFor(nameof(CompressCommand))]
    private bool _isCompressing;

    public CompressToolViewModel(EditorViewModel editor, DocumentSaver saver, DialogService dialogs)
        : base(editor)
    {
        _saver = saver;
        _dialogs = dialogs;
        Formats =
        [
            new FormatChoice("Same as original", ImageFormat.Unknown),
            new FormatChoice("JPEG", ImageFormat.Jpeg),
            new FormatChoice("WebP", ImageFormat.WebP),
            new FormatChoice("PNG", ImageFormat.Png),
        ];
        _selectedFormat = Formats[0];
    }

    public override string Name => "Compress";

    public override string Glyph => "";

    public IReadOnlyList<FormatChoice> Formats { get; }

    public bool IsQualityMode => Mode == CompressMode.Quality;

    public bool IsTargetMode => Mode == CompressMode.TargetSize;

    public bool UsesQuality => OutputFormat != ImageFormat.Png;

    public bool CanShowResult => !IsCompressing && _lastOutput is not null;

    public string? FormatNote => OutputFormat == ImageFormat.Png
        ? "PNG keeps every pixel, so quality doesn't apply. For much smaller files, choose WebP or JPEG. A target size will reduce the dimensions instead."
        : null;

    private ImageFormat OutputFormat
    {
        get
        {
            if (SelectedFormat.Format != ImageFormat.Unknown)
            {
                return SelectedFormat.Format;
            }

            var source = Editor.Document?.Source.Format ?? ImageFormat.Jpeg;
            return ImageFormats.CanEncode(source) ? source : ImageFormat.Jpeg;
        }
    }

    public override void Activate()
    {
        ResultText = null;
        ProblemText = null;
        Refresh();
    }

    public override void Deactivate()
    {
        _estimator.Cancel();
        _running?.Cancel();
    }

    public override void OnImageChanged() => Refresh();

    partial void OnPresetChanged(CompressionPreset value) => RefreshEstimate();

    partial void OnModeChanged(CompressMode value) => RefreshEstimate();

    partial void OnTargetTextChanged(string value) => RefreshEstimate();

    partial void OnSelectedFormatChanged(FormatChoice value) => RefreshEstimate();

    private void Refresh()
    {
        OnPropertyChanged(nameof(FormatNote));
        OnPropertyChanged(nameof(UsesQuality));
        if (Editor.Document is { } document)
        {
            OriginalText = document.Source.FileSize > 0 ? FileSizeText.Format(document.Source.FileSize) : "Not saved yet";
        }

        RefreshEstimate();
    }

    private async void RefreshEstimate()
    {
        if (Mode == CompressMode.TargetSize)
        {
            _estimator.Cancel();
            EstimateText = TryParseTarget(out var bytes) ? $"Under {FileSizeText.Format(bytes)}" : "Enter a size in MB";
            return;
        }

        if (Editor.Document is not { } document)
        {
            return;
        }

        EstimateText = "Estimating...";
        var options = new EncodeOptions(OutputFormat, Compression.QualityFor(OutputFormat, Preset), SKColors.White);
        var size = await _estimator.EstimateAsync(document.RenderFlattened(), pixels => ImageEncoder.Encode(pixels, options).LongLength);
        if (size is { } estimated)
        {
            EstimateText = $"About {FileSizeText.Format(estimated)}";
        }
    }

    private bool TryParseTarget(out long bytes)
    {
        bytes = 0;
        if (!double.TryParse(TargetText, NumberStyles.Float, CultureInfo.CurrentCulture, out var megabytes) || megabytes <= 0)
        {
            return false;
        }

        bytes = (long)(megabytes * 1024 * 1024);
        return bytes > 0;
    }

    private bool CanCompress() => !IsCompressing && (IsQualityMode || TryParseTarget(out _));

    [RelayCommand(CanExecute = nameof(CanCompress))]
    private async Task CompressAsync()
    {
        if (Editor.Document is not { } document)
        {
            return;
        }

        Editor.FlushPendingEdits();
        ResultText = null;
        ProblemText = null;
        _lastOutput = null;

        var format = OutputFormat;
        using var flattened = document.RenderFlattened();

        SKColor? background = null;
        if (format == ImageFormat.Jpeg && flattened.HasTransparency())
        {
            background = _saver.AskForBackground();
            if (background is null)
            {
                return;
            }
        }

        var target = document.Source.Path is { } path
            ? ExportService.OutputPathFor(path, "compressed", format)
            : _dialogs.PickSavePath("Pasted image-compressed", format);

        if (target is null)
        {
            return;
        }

        _running = new CancellationTokenSource();
        var cancellation = _running.Token;
        var preset = Preset;
        var byTarget = IsTargetMode && TryParseTarget(out _);
        TryParseTarget(out var targetBytes);
        var exif = _saver.ExifToKeep(document, format);

        IsCompressing = true;
        try
        {
            var outcome = await Task.Run(
                () => byTarget
                    ? Compression.ToTargetSize(flattened, format, targetBytes, background, null, cancellation)
                    : AtPreset(flattened, format, preset, background),
                cancellation);

            if (byTarget && !outcome.MetTarget)
            {
                ProblemText = $"Couldn't get under {FileSizeText.Format(targetBytes)}. The smallest PicKit could reach was {FileSizeText.Format(outcome.Size)}.";
                return;
            }

            var data = exif is not null ? Core.Metadata.ExifCarryOver.Insert(outcome.Data, exif) : outcome.Data;
            await Task.Run(() => ExportService.WriteBytes(data, target), CancellationToken.None);

            _lastOutput = target;
            ResultText = Describe(document, target, data.LongLength, outcome);
            OnPropertyChanged(nameof(CanShowResult));
        }
        catch (OperationCanceledException)
        {
            ProblemText = "Cancelled.";
        }
        catch (ImageException ex)
        {
            Log.Warn("Compress failed", ex);
            ProblemText = ex.Message;
        }
        finally
        {
            IsCompressing = false;
            _running = null;
        }
    }

    private static CompressionResult AtPreset(ImageBuffer image, ImageFormat format, CompressionPreset preset, SKColor? background)
    {
        var quality = Compression.QualityFor(format, preset);
        var data = ImageEncoder.Encode(image, new EncodeOptions(format, quality, background));
        return new CompressionResult(data, image.Width, image.Height, quality, true);
    }

    private static string Describe(EditorDocument document, string path, long size, CompressionResult outcome)
    {
        var text = $"Saved {Path.GetFileName(path)}\n{FileSizeText.Format(size)}";

        var original = document.Source.FileSize;
        if (original > size)
        {
            text += $", {100 - (size * 100 / original)}% smaller";
        }

        if (outcome.Width != document.Pixels.Width || outcome.Height != document.Pixels.Height)
        {
            text += $"\nResized to {outcome.Width} × {outcome.Height} to reach the target.";
        }

        return text;
    }

    [RelayCommand]
    private void Cancel() => _running?.Cancel();

    [RelayCommand]
    private void ShowInFolder()
    {
        if (_lastOutput is { } path)
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
    }
}
