using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Core.Diagnostics;
using PicKit.Core.Files;
using PicKit.Core.Imaging;
using PicKit.Services;
using SkiaSharp;

namespace PicKit.ViewModels.Tools;

public enum TransparencyFill
{
    White,
    Black,
    Custom,
}

public sealed partial class ConvertToolViewModel : ToolViewModel
{
    private readonly DocumentSaver _saver;
    private readonly DialogService _dialogs;
    private readonly ColorService _colors = new();
    private string? _lastOutput;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesQuality))]
    [NotifyPropertyChangedFor(nameof(NeedsFill))]
    private FormatChoice _selectedFormat;

    [ObservableProperty]
    private double _quality = 85;

    [ObservableProperty]
    private TransparencyFill _fill = TransparencyFill.White;

    [ObservableProperty]
    private System.Windows.Media.Color _customFill = System.Windows.Media.Colors.White;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsFill))]
    private bool _hasTransparency;

    [ObservableProperty]
    private string? _resultText;

    [ObservableProperty]
    private string? _problemText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConvertCommand))]
    private bool _isConverting;

    public ConvertToolViewModel(EditorViewModel editor, DocumentSaver saver, DialogService dialogs)
        : base(editor)
    {
        _saver = saver;
        _dialogs = dialogs;
        Formats =
        [
            new FormatChoice("PNG", ImageFormat.Png),
            new FormatChoice("JPEG", ImageFormat.Jpeg),
            new FormatChoice("WebP", ImageFormat.WebP),
        ];
        _selectedFormat = Formats[0];
    }

    public override string Name => "Convert";

    public override string Glyph => "";

    public IReadOnlyList<FormatChoice> Formats { get; }

    public bool UsesQuality => SelectedFormat.Format != ImageFormat.Png;

    /// <summary>Only asks about transparency when there's transparency to lose.</summary>
    public bool NeedsFill => SelectedFormat.Format == ImageFormat.Jpeg && HasTransparency;

    public bool CanShowResult => _lastOutput is not null;

    public override void Activate()
    {
        ResultText = null;
        ProblemText = null;
        Refresh();
    }

    public override void OnImageChanged() => Refresh();

    private void Refresh()
    {
        if (Editor.Document is { } document)
        {
            using var flattened = document.RenderFlattened();
            HasTransparency = flattened.HasTransparency();
        }
    }

    private SKColor FillColor() => Fill switch
    {
        TransparencyFill.Black => SKColors.Black,
        TransparencyFill.Custom => new SKColor(CustomFill.R, CustomFill.G, CustomFill.B),
        _ => SKColors.White,
    };

    private bool CanConvert() => !IsConverting;

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private async Task ConvertAsync()
    {
        if (Editor.Document is not { } document)
        {
            return;
        }

        Editor.FlushPendingEdits();
        ResultText = null;
        ProblemText = null;

        var format = SelectedFormat.Format;
        var suffix = format switch
        {
            ImageFormat.Png when HasTransparency => "transparent",
            ImageFormat.Jpeg => "jpeg",
            ImageFormat.WebP => "webp",
            _ => "png",
        };

        var target = document.Source.Path is { } path
            ? ExportService.OutputPathFor(path, suffix, format)
            : _dialogs.PickSavePath($"Pasted image-{suffix}", format);

        if (target is null)
        {
            return;
        }

        var options = new EncodeOptions(format, (int)Quality, FillColor());
        var exif = _saver.ExifToKeep(document, format);

        IsConverting = true;
        using var flattened = document.RenderFlattened();
        try
        {
            var result = await Task.Run(() => ExportService.Save(flattened, options, target, exif));
            _lastOutput = result.Path;
            ResultText = $"Saved {Path.GetFileName(result.Path)}\n{FileSizeText.Format(result.Size)}";
            OnPropertyChanged(nameof(CanShowResult));
        }
        catch (ImageException ex)
        {
            Log.Warn("Convert failed", ex);
            ProblemText = ex.Message;
        }
        finally
        {
            IsConverting = false;
        }
    }

    [RelayCommand]
    private void ChooseCustomFill()
    {
        if (_colors.Pick(CustomFill) is { } picked)
        {
            CustomFill = picked;
            Fill = TransparencyFill.Custom;
        }
    }

    [RelayCommand]
    private void ShowInFolder()
    {
        if (_lastOutput is { } path)
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
    }
}
