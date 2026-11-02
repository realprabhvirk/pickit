using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Core.Background;
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
    private readonly ImageInbox _inbox;
    private readonly BackgroundSettingsStore _background;
    private CancellationTokenSource? _noticeTimer;
    private bool _isSyncingSelection;
    private bool _hasHandledLaunch;

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

    /// <summary>The inbox file behind the open image, while it hasn't been saved or trashed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWaitingImage))]
    private string? _waitingPath;

    public MainViewModel(
        SettingsStore settings,
        DialogService dialogs,
        ClipboardService clipboard,
        RecentFiles? recent = null,
        ImageInbox? inbox = null,
        BackgroundSettingsStore? background = null)
    {
        _settings = settings;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _recent = recent ?? new RecentFiles();
        _inbox = inbox ?? new ImageInbox(trash: RecycleBin.Send);
        _background = background ?? new BackgroundSettingsStore();
        RefreshRecent();
        _saver = new DocumentSaver(settings, dialogs);
        Editor = new EditorViewModel(dialogs, _saver);
        Editor.DocumentContentChanged += OnDocumentContentChanged;
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

    public bool IsWaitingImage => WaitingPath is not null;

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
    public void Handle(LaunchRequest request, bool bringToFront = true)
    {
        _hasHandledLaunch = true;
        if (bringToFront)
        {
            ActivationRequested?.Invoke();
        }

        // Opened from Windows with nothing to open, so anything waiting comes up first.
        if (request.Action == ShellAction.Open && request.Paths.Count == 0)
        {
            ShowWaitingImages();
            return;
        }

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

    /// <summary>
    /// Brings up anything waiting in the inbox. An empty window opens the newest; otherwise
    /// they join the strip, so whatever's being worked on isn't pulled away.
    /// </summary>
    public void ShowWaitingImages()
    {
        if (!_hasHandledLaunch || IsLoading)
        {
            return;
        }

        var waiting = _inbox.Waiting.Where(path => OpenImages.All(image => image.Path != path)).ToList();
        if (waiting.Count == 0)
        {
            return;
        }

        if (Editor.Document is null && Page is null)
        {
            SetOpenImages(waiting);
            _ = LoadAsync(waiting[0]);
            return;
        }

        foreach (var path in waiting)
        {
            OpenImages.Add(new OpenImage(path));
        }

        OnOpenImagesChanged();
    }

    /// <summary>Opens whatever was dragged in, fetching it first if only its address came.</summary>
    public async Task OpenDroppedAsync(DroppedItems items)
    {
        if (items.Paths.Count > 0)
        {
            OpenPaths(items.Paths);
            return;
        }

        var file = items.File;
        if (file is null && items.Url is { } address)
        {
            IsLoading = true;
            file = await ImageFetcher.FetchAsync(address);
            IsLoading = false;
        }

        if ((file is null ? null : _inbox.AddDropped(file)) is { } path)
        {
            OpenPaths([path]);
        }
        else
        {
            ShowNotice("That isn't an image PicKit can open");
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
        RefreshRecent,
        _background,
        new BackgroundControl(),
        _dialogs);

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
            UnsavedChoice.Save => await SaveCurrentAsync(),
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
    private Task Save() => SaveCurrentAsync();

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

    private Task<bool> SaveCurrentAsync() => IsWaitingImage ? KeepWaitingImageAsync() : _saver.SaveAsync(Editor);

    /// <summary>Moves a waiting image out to the save folder, along with any edits made to it.</summary>
    [RelayCommand]
    private async Task KeepWaitingAsync() => await KeepWaitingImageAsync();

    private async Task<bool> KeepWaitingImageAsync()
    {
        if (WaitingPath is not { } path || Editor.Document is not { } document)
        {
            return false;
        }

        Editor.FlushPendingEdits();

        string kept;
        try
        {
            kept = _inbox.Keep(path, _background.SaveFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not save a waiting image", ex);
            _dialogs.ShowError("Couldn't save", "PicKit couldn't move this image to its folder. Use Save As to choose another.", ex.Message);
            return false;
        }

        WaitingPath = null;
        ReplaceInStrip(path, kept);
        RememberRecent(kept);

        var saved = true;
        if (document.IsDirty)
        {
            document.UpdateSourcePath(kept);
            saved = await _saver.SaveAsync(Editor, confirmReplace: false);
        }
        else
        {
            document.MarkSaved(document.Source with { Path = kept, Name = null });
        }

        RefreshDocumentText();
        if (saved)
        {
            ShowNotice($"Saved to {Path.GetDirectoryName(kept)}");
        }

        return saved;
    }

    [RelayCommand]
    private async Task TrashWaitingAsync()
    {
        if (WaitingPath is not { } path)
        {
            return;
        }

        await Editor.WaitUntilIdleAsync();
        try
        {
            _inbox.Trash(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            Log.Warn("Could not trash a waiting image", ex);
            _dialogs.ShowError("Couldn't move it to the Recycle Bin", "PicKit couldn't remove this image.", ex.Message);
            return;
        }

        WaitingPath = null;
        var remaining = OpenImages.Where(image => image.Path != path).Select(image => image.Path).ToList();
        SetOpenImages(remaining);
        if (remaining.Count > 0)
        {
            await LoadAsync(remaining[0]);
        }
        else
        {
            CloseDocument();
        }
    }

    private void ReplaceInStrip(string from, string to)
    {
        for (var i = 0; i < OpenImages.Count; i++)
        {
            if (OpenImages[i].Path == from)
            {
                var wasSelected = ReferenceEquals(SelectedImage, OpenImages[i]);
                _isSyncingSelection = true;
                OpenImages[i] = new OpenImage(to);
                if (wasSelected)
                {
                    SelectedImage = OpenImages[i];
                }

                _isSyncingSelection = false;
            }
        }
    }

    private void CloseDocument()
    {
        var document = Editor.Document;
        Editor.Document = null;
        document?.Dispose();
        RefreshDocumentText();
    }

    private void OnDocumentContentChanged()
    {
        // Save As gave it a real file somewhere else, so the waiting copy has done its job.
        if (WaitingPath is { } path && Editor.Document?.Source.Path is { } saved)
        {
            SafeFile.TryDelete(path);
            WaitingPath = null;
            ReplaceInStrip(path, saved);
        }

        RefreshDocumentText();
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

    private void ShowDocument(EditorDocument document, string? waitingPath = null)
    {
        App.EnsurePanelsLoaded();
        var previous = Editor.Document;
        Editor.Document = document;
        previous?.Dispose();
        WaitingPath = waitingPath;
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

        OnOpenImagesChanged();

        _isSyncingSelection = true;
        SelectedImage = OpenImages.FirstOrDefault();
        _isSyncingSelection = false;
    }

    private void OnOpenImagesChanged()
    {
        OnPropertyChanged(nameof(HasMultipleImages));
        BatchAllCommand.NotifyCanExecuteChanged();
        RenameAllCommand.NotifyCanExecuteChanged();
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

            // A waiting image has no file of its own yet, so nothing can save back into the inbox.
            var isWaiting = _inbox.Contains(path);
            var source = isWaiting
                ? new DocumentSource(null, decoded.Format, size, decoded.FrameCount, Path.GetFileName(path))
                : new DocumentSource(path, decoded.Format, size, decoded.FrameCount);

            await Editor.WaitUntilIdleAsync();

            ShowDocument(new EditorDocument(decoded.Pixels, source), isWaiting ? path : null);
            if (!isWaiting)
            {
                RememberRecent(path);
            }

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
