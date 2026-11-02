using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Core.Batch;
using PicKit.Core.Diagnostics;
using PicKit.Core.Documents;
using PicKit.Core.Files;
using PicKit.Core.Imaging;
using PicKit.Core.Settings;
using PicKit.Core.Shell;
using PicKit.Services;
using SkiaSharp;

namespace PicKit.ViewModels;

/// <summary>One image in the strip shown when several are open.</summary>
public sealed record OpenImage(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(2.5);

    private readonly SettingsStore _settings;
    private readonly DialogService _dialogs;
    private readonly DocumentSaver _saver;
    private readonly ClipboardService _clipboard;
    private readonly RecentFiles _recent;
    private CancellationTokenSource? _noticeTimer;
    private bool _isSyncingSelection;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _canPasteFromClipboard;

    [ObservableProperty]
    private string? _noticeText;

    /// <summary>A full-window job, such as a batch or rename, shown over the editor.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPage))]
    [NotifyPropertyChangedFor(nameof(ShowsEditorCommands))]
    private ObservableObject? _page;

    [ObservableProperty]
    private OpenImage? _selectedImage;

    public MainViewModel(SettingsStore settings, DialogService dialogs, ClipboardService clipboard, RecentFiles? recent = null)
    {
        _settings = settings;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _recent = recent ?? new RecentFiles();
        RefreshRecent();
        _saver = new DocumentSaver(settings, dialogs);
        Editor = new EditorViewModel(dialogs, _saver);
        Editor.DocumentContentChanged += RefreshDocumentText;
    }

    /// <summary>Asks the window to come to the front.</summary>
    public event Action? ActivationRequested;

    public EditorViewModel Editor { get; }

    public bool HasImage => Editor.Document is not null;

    public bool HasPage => Page is not null;

    /// <summary>Editing commands are hidden while a batch or rename job covers the editor.</summary>
    public bool ShowsEditorCommands => HasImage && !HasPage;

    public ObservableCollection<OpenImage> OpenImages { get; } = [];

    public ObservableCollection<OpenImage> RecentImages { get; } = [];

    public bool HasRecentImages => RecentImages.Count > 0;

    public bool HasMultipleImages => OpenImages.Count > 1;

    /// <summary>Where the last rename is remembered. Replaced in tests.</summary>
    public string? RenameHistoryPath { get; init; }

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

    public void OpenPaths(IReadOnlyList<string> paths) => Handle(new LaunchRequest(ShellAction.Open, paths));

    /// <summary>Carries out what Explorer or the command line asked for, and brings the window forward.</summary>
    public void Handle(LaunchRequest request)
    {
        ActivationRequested?.Invoke();

        var images = ImagePaths.Expand(request.Paths);
        if (request.Paths.Count > 0 && images.Count == 0)
        {
            ShowNotice("That file isn't an image PicKit can open");
        }
        else if (images.Count > 0)
        {
            _ = HandleAsync(request.Action, images);
        }
    }

    public void RememberRecent(string path)
    {
        _recent.Add(path);
        RefreshRecent();
    }

    public void ClearRecent()
    {
        _recent.Clear();
        RefreshRecent();
    }

    public SettingsViewModel CreateSettings() => new(
        _settings,
        _recent,
        ShellRegistration.ForCurrentUser(Environment.ProcessPath!),
        RefreshRecent);

    /// <summary>Keeps Close safe while a batch is running.</summary>
    public void CancelBackgroundWork() => (Page as BatchViewModel)?.CancelIfRunning();

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
        Page = null;
        SetOpenImages([]);
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
        App.EnsurePanelsLoaded();
        var previous = Editor.Document;
        Editor.Document = document;
        previous?.Dispose();
        RefreshDocumentText();
    }

    private async Task HandleAsync(ShellAction action, IReadOnlyList<string> images)
    {
        switch (action)
        {
            case ShellAction.Copy:
                await CopyFileAsync(images[0]);
                if (images.Count > 1)
                {
                    ShowNotice("Copied the first image. The clipboard holds one at a time");
                }

                return;

            case ShellAction.Rename:
                ShowRename(images);
                return;

            case ShellAction.Compress when images.Count > 1:
                ShowBatch(images, BatchOperation.Compress);
                return;

            case ShellAction.Resize when images.Count > 1:
                ShowBatch(images, BatchOperation.Resize);
                return;

            case ShellAction.Convert when images.Count > 1:
                ShowBatch(images, BatchOperation.Convert);
                return;
        }

        if (!await ResolveUnsavedChangesAsync())
        {
            return;
        }

        Page = null;
        SetOpenImages(images);
        await LoadAsync(images[0]);
        ShowToolFor(action);
    }

    private void RefreshRecent()
    {
        RecentImages.Clear();
        foreach (var path in _recent.Existing)
        {
            RecentImages.Add(new OpenImage(path));
        }

        OnPropertyChanged(nameof(HasRecentImages));
    }

    [RelayCommand]
    private void OpenRecent(OpenImage image) => OpenPaths([image.Path]);

    private void ShowToolFor(ShellAction action)
    {
        switch (action)
        {
            case ShellAction.Compress:
                ShowTool(EditorTab.Tools, Editor.Compress);
                break;
            case ShellAction.Convert:
                ShowTool(EditorTab.Tools, Editor.Convert);
                break;
            case ShellAction.Resize:
                ShowTool(EditorTab.Edit, Editor.Resize);
                break;
            case ShellAction.RemoveBackground:
                ShowTool(EditorTab.Edit, Editor.Background);
                break;
        }
    }

    private void SetOpenImages(IReadOnlyList<string> images)
    {
        OpenImages.Clear();
        foreach (var path in images)
        {
            OpenImages.Add(new OpenImage(path));
        }

        OnPropertyChanged(nameof(HasMultipleImages));
        BatchAllCommand.NotifyCanExecuteChanged();
        RenameAllCommand.NotifyCanExecuteChanged();

        _isSyncingSelection = true;
        SelectedImage = OpenImages.FirstOrDefault();
        _isSyncingSelection = false;
    }

    private void ShowBatch(IReadOnlyList<string> images, BatchOperation operation)
    {
        App.EnsurePanelsLoaded();
        Page = new BatchViewModel(images, operation, _settings, _dialogs, () => Page = null);
    }

    private void ShowRename(IReadOnlyList<string> images)
    {
        App.EnsurePanelsLoaded();
        Page = new RenameViewModel(images, () => Page = null, OnFilesRenamed, RenameHistoryPath);
    }

    /// <summary>Keeps the open image and the strip pointing at files that just changed name.</summary>
    private void OnFilesRenamed(IReadOnlyList<RenamedFile> files)
    {
        for (var i = 0; i < OpenImages.Count; i++)
        {
            if (files.FirstOrDefault(file => file.From == OpenImages[i].Path) is { } match)
            {
                var wasSelected = ReferenceEquals(SelectedImage, OpenImages[i]);
                _isSyncingSelection = true;
                OpenImages[i] = new OpenImage(match.To);
                if (wasSelected)
                {
                    SelectedImage = OpenImages[i];
                }

                _isSyncingSelection = false;
            }
        }

        if (Editor.Document is { Source.Path: { } path } document
            && files.FirstOrDefault(file => file.From == path) is { } renamed)
        {
            document.UpdateSourcePath(renamed.To);
            RefreshDocumentText();
        }
    }

    partial void OnSelectedImageChanged(OpenImage? oldValue, OpenImage? newValue)
    {
        if (!_isSyncingSelection && newValue is not null && newValue.Path != Editor.Document?.Source.Path)
        {
            _ = SwitchImageAsync(oldValue, newValue);
        }
    }

    private async Task SwitchImageAsync(OpenImage? previous, OpenImage next)
    {
        if (await ResolveUnsavedChangesAsync())
        {
            await LoadAsync(next.Path);
        }
        else
        {
            // The user chose to stay, so the strip goes back to the image still on screen.
            _isSyncingSelection = true;
            SelectedImage = previous;
            _isSyncingSelection = false;
        }
    }

    private bool CanRunOnAllImages() => HasMultipleImages;

    [RelayCommand(CanExecute = nameof(CanRunOnAllImages))]
    private void BatchAll(BatchOperation operation) => ShowBatch(OpenImages.Select(image => image.Path).ToList(), operation);

    [RelayCommand(CanExecute = nameof(CanRunOnAllImages))]
    private void RenameAll() => ShowRename(OpenImages.Select(image => image.Path).ToList());

    private void ShowTool(EditorTab tab, ToolViewModel tool)
    {
        if (HasImage)
        {
            Editor.SelectedTab = tab;
            Editor.ActiveTool = tool;
        }
    }

    /// <summary>Copies a file's picture to the clipboard, without disturbing whatever is open.</summary>
    private async Task CopyFileAsync(string path)
    {
        try
        {
            var background = SKColor.TryParse(_settings.Current.TransparencyBackground, out var parsed) ? parsed : SKColors.White;
            var items = await Task.Run(() =>
            {
                using var decoded = ImageDecoder.Decode(path);
                return ClipboardCodec.Encode(decoded.Pixels, CopyMode.Standard, background);
            });

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
            Log.Warn($"Could not copy {path}", ex);
            _dialogs.ShowError("Couldn't copy", ex.Message, ex.Details);
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
            RememberRecent(path);

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
        OnPropertyChanged(nameof(ShowsEditorCommands));
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
