using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Core.Diagnostics;
using PicKit.Core.Documents;
using PicKit.Core.Files;
using PicKit.Core.Imaging;
using PicKit.Core.Settings;
using PicKit.Services;

namespace PicKit.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly SettingsStore _settings;
    private readonly DialogService _dialogs;
    private readonly DocumentSaver _saver;

    [ObservableProperty]
    private bool _isLoading;

    public MainViewModel(SettingsStore settings, DialogService dialogs)
    {
        _settings = settings;
        _dialogs = dialogs;
        _saver = new DocumentSaver(settings, dialogs);
        Editor = new EditorViewModel(dialogs, _saver);
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

    /// <summary>True if closing now would lose work, counting anything still marked by a tool.</summary>
    public bool HasUnsavedChanges
    {
        get
        {
            Editor.FlushPendingEdits();
            return Editor.Document?.IsDirty == true;
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

            var parts = new List<string>
            {
                $"{document.Pixels.Width} × {document.Pixels.Height}",
                ImageFormats.DisplayName(document.Source.Format),
            };

            if (document.Source.FileSize > 0)
            {
                parts.Add(FileSizeText.Format(document.Source.FileSize));
            }

            if (document.Source.FrameCount > 1)
            {
                parts.Add($"Animated: showing the first of {document.Source.FrameCount} frames");
            }

            return string.Join("  ·  ", parts);
        }
    }

    public void OpenPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count > 0)
        {
            _ = OpenAsync(paths[0]);
        }
    }

    /// <summary>
    /// Deals with unsaved edits before they're lost. Returns false if the user wants to stay.
    /// </summary>
    public async Task<bool> ResolveUnsavedChangesAsync()
    {
        if (Editor.Document is not { } document)
        {
            return true;
        }

        Editor.FlushPendingEdits();
        if (!document.IsDirty)
        {
            return true;
        }

        return _dialogs.AskAboutUnsavedChanges(document.Source.DisplayName) switch
        {
            UnsavedChoice.Save => await _saver.SaveAsync(Editor),
            UnsavedChoice.Discard => true,
            _ => false,
        };
    }

    private bool CanSave() => HasImage && Editor.IsIdle;

    [RelayCommand]
    private void Open() => OpenPaths(_dialogs.PickImages());

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task Save() => _saver.SaveAsync(Editor);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveCopy() => _saver.SaveCopyAsync(Editor);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAs() => _saver.SaveAsAsync(Editor);

    private async Task OpenAsync(string path)
    {
        if (await ResolveUnsavedChangesAsync())
        {
            await LoadAsync(path);
        }
    }

    private async Task LoadAsync(string path)
    {
        IsLoading = true;
        try
        {
            var decoded = await Task.Run(() => ImageDecoder.Decode(path));
            var size = new FileInfo(path).Length;
            var source = new DocumentSource(path, decoded.Format, size, decoded.FrameCount);

            await Editor.WaitUntilIdleAsync();

            var previous = Editor.Document;
            Editor.Document = new EditorDocument(decoded.Pixels, source);
            previous?.Dispose();

            if (decoded.IsIncomplete)
            {
                _dialogs.ShowError(
                    "This image looks incomplete",
                    "Part of the file seems to be missing, so some of the picture may be blank. Saving keeps only what you can see.");
            }

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
        SaveCommand.NotifyCanExecuteChanged();
        SaveCopyCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
    }
}
