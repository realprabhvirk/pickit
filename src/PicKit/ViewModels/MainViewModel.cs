using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Core.Diagnostics;
using PicKit.Core.Documents;
using PicKit.Core.Files;
using PicKit.Core.Imaging;
using PicKit.Core.Settings;
using PicKit.Services;
using SkiaSharp;

namespace PicKit.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(2.5);

    private readonly SettingsStore _settings;
    private readonly DialogService _dialogs;
    private readonly DocumentSaver _saver;
    private readonly ClipboardService _clipboard;
    private CancellationTokenSource? _noticeTimer;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _canPasteFromClipboard;

    [ObservableProperty]
    private string? _noticeText;

    public MainViewModel(SettingsStore settings, DialogService dialogs, ClipboardService clipboard)
    {
        _settings = settings;
        _dialogs = dialogs;
        _clipboard = clipboard;
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

    /// <summary>Checks the clipboard, so the empty window can offer a screenshot that's waiting there.</summary>
    public void RefreshClipboardState() => CanPasteFromClipboard = _clipboard.ContainsImage();

    private bool CanSave() => HasImage && Editor.IsIdle;

    [RelayCommand]
    private void Open() => OpenPaths(_dialogs.PickImages());

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task Save() => _saver.SaveAsync(Editor);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveCopy() => _saver.SaveCopyAsync(Editor);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveAs() => _saver.SaveAsAsync(Editor);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task Copy() => CopyToClipboardAsync(CopyMode.Standard);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task CopyAsPng() => CopyToClipboardAsync(CopyMode.Png);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task CopyAsJpeg() => CopyToClipboardAsync(CopyMode.Jpeg);

    [RelayCommand]
    private async Task PasteAsync()
    {
        var files = _clipboard.ReadFiles();
        if (files.Count > 0)
        {
            OpenPaths(files);
            return;
        }

        if (!_clipboard.ContainsImage())
        {
            ShowNotice("No image on the clipboard");
            return;
        }

        if (!await ResolveUnsavedChangesAsync())
        {
            return;
        }

        var image = _clipboard.ReadImage();
        if (image is null)
        {
            ShowNotice("No image on the clipboard");
            return;
        }

        await Editor.WaitUntilIdleAsync();
        ShowDocument(new EditorDocument(image, new DocumentSource(null, ImageFormat.Png, 0)));
    }

    private async Task CopyToClipboardAsync(CopyMode mode)
    {
        if (Editor.Document is not { } document)
        {
            return;
        }

        Editor.FlushPendingEdits();
        Editor.BeginBusy("Copying...");
        try
        {
            using var flattened = document.RenderFlattened();
            var background = SKColor.TryParse(_settings.Current.TransparencyBackground, out var parsed) ? parsed : SKColors.White;
            var items = await Task.Run(() => ClipboardCodec.Encode(flattened, mode, background));

            if (_clipboard.Set(items))
            {
                ShowNotice("Copied to clipboard");
            }
            else
            {
                _dialogs.ShowError("Couldn't copy", "Another app is using the clipboard. Try again in a moment.");
            }
        }
        catch (ImageException ex)
        {
            Log.Warn("Copy failed", ex);
            _dialogs.ShowError("Couldn't copy", ex.Message, ex.Details);
        }
        finally
        {
            Editor.EndBusy();
        }
    }

    private async void ShowNotice(string text)
    {
        _noticeTimer?.Cancel();
        var timer = new CancellationTokenSource();
        _noticeTimer = timer;
        NoticeText = text;

        try
        {
            await Task.Delay(NoticeDuration, timer.Token);
            NoticeText = null;
        }
        catch (OperationCanceledException)
        {
            // A newer notice took over.
        }
    }

    private void ShowDocument(EditorDocument document)
    {
        var previous = Editor.Document;
        Editor.Document = document;
        previous?.Dispose();
        RefreshDocumentText();
    }

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

            ShowDocument(new EditorDocument(decoded.Pixels, source));

            if (decoded.IsIncomplete)
            {
                _dialogs.ShowError(
                    "This image looks incomplete",
                    "Part of the file seems to be missing, so some of the picture may be blank. Saving keeps only what you can see.");
            }
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
        CopyCommand.NotifyCanExecuteChanged();
        CopyAsPngCommand.NotifyCanExecuteChanged();
        CopyAsJpegCommand.NotifyCanExecuteChanged();
    }
}
