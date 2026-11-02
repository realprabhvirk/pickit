using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Core.Diagnostics;
using PicKit.Core.Files;
using PicKit.Core.Imaging;
using PicKit.Core.Metadata;

namespace PicKit.ViewModels.Tools;

/// <summary>Shows what a file reveals, and writes a copy that reveals nothing.</summary>
public sealed partial class DetailsToolViewModel : ToolViewModel
{
    private string? _lastOutput;

    [ObservableProperty]
    private IReadOnlyList<MetadataField> _fields = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    private bool _hasPrivateData;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string? _resultText;

    [ObservableProperty]
    private string? _problemText;

    public DetailsToolViewModel(EditorViewModel editor)
        : base(editor)
    {
    }

    public override string Name => "Details";

    public override string Glyph => "";

    public bool CanRemove => HasPrivateData;

    public bool CanShowResult => _lastOutput is not null;

    public override void Activate()
    {
        ResultText = null;
        ProblemText = null;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        Fields = [];
        HasPrivateData = false;

        if (Editor.Document?.Source.Path is not { } path)
        {
            Summary = "Pasted images have no file details.";
            return;
        }

        Summary = "Reading...";
        var info = await Task.Run(() => ImageMetadataInfo.Read(path));

        Fields = info.Fields;
        HasPrivateData = info.HasPrivateData;
        Summary = info.HasLocation
            ? "This file records where it was taken."
            : info.HasPrivateData
                ? "This file contains details about how it was made."
                : "This file has no camera, location or author details.";
    }

    private bool CanRunRemove() => HasPrivateData;

    [RelayCommand(CanExecute = nameof(CanRunRemove))]
    private async Task RemoveAsync()
    {
        if (Editor.Document?.Source.Path is not { } path)
        {
            return;
        }

        ResultText = null;
        ProblemText = null;
        var format = ImageFormats.FromExtension(path);
        var target = ExportService.OutputPathFor(path, "clean", ImageFormats.CanEncode(format) ? format : ImageFormat.Png);

        try
        {
            await Task.Run(() => ExportService.WriteClean(path, target));

            // Report only what's verifiably true of the file that was written.
            var check = await Task.Run(() => ImageMetadataInfo.Read(target));
            if (check.HasPrivateData)
            {
                SafeFile.TryDelete(target);
                ProblemText = "PicKit couldn't remove all the details from this file, so no copy was kept.";
                return;
            }

            _lastOutput = target;
            ResultText = $"Saved {Path.GetFileName(target)}\nThe copy has no camera, location or author details. Your original wasn't changed.";
            OnPropertyChanged(nameof(CanShowResult));
        }
        catch (Exception ex) when (ex is ImageException or IOException or UnauthorizedAccessException)
        {
            Log.Warn("Metadata removal failed", ex);
            ProblemText = ex.Message;
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
