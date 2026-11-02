using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Core.Diagnostics;
using PicKit.Core.Imaging;
using PicKit.Core.Settings;
using PicKit.Services;

namespace PicKit.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly SettingsStore _settings;
    private readonly DialogService _dialogs;
    private DecodedImage? _current;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage))]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private ImageBuffer? _image;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string? _fileName;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    public MainViewModel(SettingsStore settings, DialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;
    }

    public bool HasImage => Image is not null;

    public string WindowTitle => FileName is null ? "PicKit" : $"{FileName} - PicKit";

    public void OpenPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count > 0)
        {
            _ = LoadAsync(paths[0]);
        }
    }

    [RelayCommand]
    private void Open()
    {
        var paths = _dialogs.PickImages();
        OpenPaths(paths);
    }

    private async Task LoadAsync(string path)
    {
        IsBusy = true;
        try
        {
            var decoded = await Task.Run(() => ImageDecoder.Decode(path));

            var previous = _current;
            _current = decoded;
            Image = decoded.Pixels;
            FileName = Path.GetFileName(path);
            StatusText = $"{decoded.Pixels.Width} × {decoded.Pixels.Height}  ·  {ImageFormats.DisplayName(decoded.Format)}";
            previous?.Dispose();
        }
        catch (ImageException ex)
        {
            Log.Warn($"Could not open {path}", ex);
            _dialogs.ShowError("Couldn't open image", ex.Message, ex.Details);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
