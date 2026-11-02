using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Core.Diagnostics;
using PicKit.Core.Documents;
using PicKit.Core.Imaging;
using PicKit.Core.Settings;
using PicKit.Services;

namespace PicKit.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly SettingsStore _settings;
    private readonly DialogService _dialogs;

    [ObservableProperty]
    private bool _isLoading;

    public MainViewModel(SettingsStore settings, DialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;
        Editor = new EditorViewModel(dialogs);
        Editor.DocumentContentChanged += RefreshDocumentText;
    }

    public EditorViewModel Editor { get; }

    public bool HasImage => Editor.Document is not null;

    public string WindowTitle
    {
        get
        {
            if (Editor.Document is not { } document)
            {
                return "PicKit";
            }

            return $"{(document.IsDirty ? "● " : string.Empty)}{document.Source.DisplayName} - PicKit";
        }
    }

    public string FileName => Editor.Document?.Source.DisplayName ?? string.Empty;

    public string StatusText
    {
        get
        {
            if (Editor.Document is not { } document)
            {
                return string.Empty;
            }

            var format = ImageFormats.DisplayName(document.Source.Format);
            return $"{document.Pixels.Width} × {document.Pixels.Height}  ·  {format}";
        }
    }

    public void OpenPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count > 0)
        {
            _ = LoadAsync(paths[0]);
        }
    }

    [RelayCommand]
    private void Open() => OpenPaths(_dialogs.PickImages());

    private async Task LoadAsync(string path)
    {
        IsLoading = true;
        try
        {
            var decoded = await Task.Run(() => ImageDecoder.Decode(path));
            var size = new FileInfo(path).Length;
            var source = new DocumentSource(path, decoded.Format, size, decoded.FrameCount);

            var previous = Editor.Document;
            Editor.Document = new EditorDocument(decoded.Pixels, source);
            previous?.Dispose();

            RefreshDocumentText();
        }
        catch (ImageException ex)
        {
            Log.Warn($"Could not open {path}", ex);
            _dialogs.ShowError("Couldn't open image", ex.Message, ex.Details);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RefreshDocumentText()
    {
        OnPropertyChanged(nameof(HasImage));
        OnPropertyChanged(nameof(WindowTitle));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(StatusText));
    }
}
