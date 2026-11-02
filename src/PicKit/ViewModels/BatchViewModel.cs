using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Core.Batch;
using PicKit.Core.Imaging;
using PicKit.Core.Settings;
using PicKit.Services;
using PicKit.ViewModels.Tools;
using SkiaSharp;

namespace PicKit.ViewModels;

public sealed record BatchFailure(string Name, string Reason);

/// <summary>Sets up and runs one operation over many images, without blocking the window.</summary>
public sealed partial class BatchViewModel : ObservableObject
{
    private readonly IReadOnlyList<string> _paths;
    private readonly SettingsStore _settings;
    private readonly DialogService _dialogs;
    private readonly Action _close;
    private CancellationTokenSource? _running;
    private string? _outputFolder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading))]
    [NotifyPropertyChangedFor(nameof(IsCompress))]
    [NotifyPropertyChangedFor(nameof(IsResize))]
    [NotifyPropertyChangedFor(nameof(IsConvert))]
    private BatchOperation _operation;

    [ObservableProperty]
    private CompressionPreset _preset = CompressionPreset.Balanced;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsKeepAspect))]
    [NotifyPropertyChangedFor(nameof(CropNote))]
    private ResizeMode _resizeMode = ResizeMode.Fit;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string _widthText = "1920";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string _heightText = "1080";

    [ObservableProperty]
    private bool _keepAspect = true;

    [ObservableProperty]
    private FormatChoice _selectedFormat;

    [ObservableProperty]
    private double _quality = 85;

    [ObservableProperty]
    private BatchOutput _output = BatchOutput.SameFolder;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string? _chosenFolder;

    [ObservableProperty]
    private bool _preserveNames = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetup))]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSetup))]
    private bool _isFinished;

    [ObservableProperty]
    private int _completed;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _resultText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<BatchFailure> _failures = [];

    public BatchViewModel(
        IReadOnlyList<string> paths, BatchOperation operation, SettingsStore settings, DialogService dialogs, Action close)
    {
        _paths = paths;
        _operation = operation;
        _settings = settings;
        _dialogs = dialogs;
        _close = close;

        Formats =
        [
            new FormatChoice("WebP", ImageFormat.WebP),
            new FormatChoice("JPEG", ImageFormat.Jpeg),
            new FormatChoice("PNG", ImageFormat.Png),
        ];
        _selectedFormat = Formats[0];
    }

    public IReadOnlyList<FormatChoice> Formats { get; }

    public int Total => _paths.Count;

    public string Heading => Operation switch
    {
        BatchOperation.Compress => $"Compress {Total} images",
        BatchOperation.Resize => $"Resize {Total} images",
        _ => $"Convert {Total} images",
    };

    public bool IsCompress => Operation == BatchOperation.Compress;

    public bool IsResize => Operation == BatchOperation.Resize;

    public bool IsConvert => Operation == BatchOperation.Convert;

    public bool IsSetup => !IsRunning && !IsFinished;

    public bool ShowsKeepAspect => ResizeMode == ResizeMode.Scale;

    public string? CropNote => ResizeMode == ResizeMode.CropToFill
        ? "Each image will be cut to this exact shape, keeping the centre. Anything outside is removed from the new copies."
        : null;

    /// <summary>Whether something is underway that closing the window should stop.</summary>
    public void CancelIfRunning() => _running?.Cancel();

    private bool CanStart()
    {
        if (IsRunning)
        {
            return false;
        }

        if (Output == BatchOutput.ChosenFolder && string.IsNullOrEmpty(ChosenFolder))
        {
            return false;
        }

        return Operation != BatchOperation.Resize || (IsValid(WidthText) && IsValid(HeightText));
    }

    private static bool IsValid(string text) =>
        int.TryParse(text, out var value) && value is > 0 and <= ImageLimits.MaxDimension;

    partial void OnOutputChanged(BatchOutput value) => StartCommand.NotifyCanExecuteChanged();

    partial void OnOperationChanged(BatchOperation value) => StartCommand.NotifyCanExecuteChanged();

    [RelayCommand]
    private void ChooseFolder()
    {
        if (_dialogs.PickFolder() is { } folder)
        {
            ChosenFolder = folder;
            Output = BatchOutput.ChosenFolder;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var options = BuildOptions();
        var running = new CancellationTokenSource();
        _running = running;

        Completed = 0;
        ProgressText = $"0 / {Total}";
        IsRunning = true;

        var progress = new Progress<BatchProgress>(update =>
        {
            Completed = update.Completed;
            ProgressText = $"{update.Completed} / {update.Total}";
        });

        try
        {
            var summary = await Task.Run(() => BatchRunner.RunAsync(_paths, options, progress, running.Token));
            ShowSummary(summary);
        }
        finally
        {
            IsRunning = false;
            IsFinished = true;
            _running = null;
        }
    }

    private BatchOptions BuildOptions()
    {
        var background = SKColor.TryParse(_settings.Current.TransparencyBackground, out var parsed) ? parsed : SKColors.White;
        int.TryParse(WidthText, out var width);
        int.TryParse(HeightText, out var height);

        return new BatchOptions
        {
            Operation = Operation,
            Format = IsConvert ? SelectedFormat.Format : ImageFormat.Unknown,
            Preset = Preset,
            Quality = (int)Quality,
            ResizeMode = ResizeMode,
            Width = width,
            Height = height,
            KeepAspect = KeepAspect,
            Background = background,
            Output = Output,
            ChosenFolder = ChosenFolder,
            PreserveNames = PreserveNames,
            KeepMetadata = _settings.Current.MetadataHandling == MetadataHandling.Keep,
        };
    }

    private void ShowSummary(BatchSummary summary)
    {
        _outputFolder = summary.OutputFolder ?? System.IO.Path.GetDirectoryName(_paths[0]);
        Failures = summary.Results
            .Where(result => !result.Succeeded)
            .Select(result => new BatchFailure(System.IO.Path.GetFileName(result.Source), result.Error!))
            .ToList();

        var saved = summary.Succeeded == 1 ? "1 image saved" : $"{summary.Succeeded} images saved";
        ResultText = summary.WasCancelled
            ? $"Cancelled. {saved}. Your originals weren't changed."
            : summary.Failed > 0
                ? $"{saved}. {summary.Failed} couldn't be processed."
                : $"Done. {saved}. Your originals weren't changed.";
    }

    [RelayCommand]
    private void Cancel() => _running?.Cancel();

    [RelayCommand]
    private void ShowFolder()
    {
        if (_outputFolder is { } folder)
        {
            Process.Start("explorer.exe", $"\"{folder}\"");
        }
    }

    [RelayCommand]
    private void Close() => _close();
}
