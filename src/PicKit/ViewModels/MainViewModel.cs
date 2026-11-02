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

/// <summary>An image in the list of recent files.</summary>
public sealed record RecentImage(string Path)
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
    private readonly HashSet<string> _closedWaitingImages = new(StringComparer.OrdinalIgnoreCase);
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
    [NotifyPropertyChangedFor(nameof(IsWaitingImage))]
    [NotifyPropertyChangedFor(nameof(WaitingPath))]
    private DocumentTab? _selectedTab;

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

    public ObservableCollection<DocumentTab> Tabs { get; } = [];

    public ObservableCollection<RecentImage> RecentImages { get; } = [];

    public bool HasTabs => Tabs.Count > 0;

    public bool HasRecentImages => RecentImages.Count > 0;

    public bool HasMultipleImages => FilesInTabs.Count > 1;

    public string? WaitingPath => SelectedTab?.WaitingPath;

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
            return Tabs.Any(tab => tab.IsDirty);
        }
    }

    /// <summary>Real files in tabs, which batch jobs can work on.</summary>
    private List<string> FilesInTabs =>
        Tabs.Where(tab => tab.Path is not null && !_inbox.Contains(tab.Path)).Select(tab => tab.Path!).ToList();

    public string FileName => Editor.Document?.Source.DisplayName ?? string.Empty;

    /// <summary>What the rename bar shows. The file type isn't part of the name people edit.</summary>
    public string EditableName => Path.GetFileNameWithoutExtension(FileName);

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

        var waiting = _inbox.Waiting
            .Where(path => !_closedWaitingImages.Contains(path) && Tabs.All(tab => tab.Path != path))
            .ToList();

        if (waiting.Count > 0)
        {
            _ = OpenInTabsAsync(waiting, showFirst: Editor.Document is null && Page is null);
        }
    }

    /// <summary>Renames the open image: the file itself if it has one, or just its name if not.</summary>
    public void RenameCurrent(string text)
    {
        if (Editor.Document is not { } document || SelectedTab is not { } tab)
        {
            return;
        }

        var stem = FileNames.Sanitize(text.Trim(), fallback: string.Empty);
        if (stem.Length > 0 && stem != EditableName)
        {
            if (document.Source.Path is { } path)
            {
                RenameFile(tab, document, path, stem);
            }
            else
            {
                document.UpdateName(stem + Path.GetExtension(document.Source.DisplayName));
            }
        }

        RefreshDocumentText();
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
    /// Goes through every tab with unsaved edits before they're lost, as the window closes.
    /// Returns false if the user wants to stay.
    /// </summary>
    public async Task<bool> ResolveUnsavedChangesAsync()
    {
        foreach (var tab in Tabs.ToList())
        {
            if (!await ResolveTabAsync(tab))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Checks the clipboard, so the empty window can offer a screenshot that's waiting there.</summary>
    public void RefreshClipboardState() => CanPasteFromClipboard = _clipboard.ContainsImage();

    private void RenameFile(DocumentTab tab, EditorDocument document, string path, string stem)
    {
        var target = Path.Combine(Path.GetDirectoryName(path)!, stem + Path.GetExtension(path));
        var isSameFile = string.Equals(target, path, StringComparison.OrdinalIgnoreCase);
        if (!isSameFile && File.Exists(target))
        {
            ShowNotice("A file with that name is already there");
            return;
        }

        try
        {
            File.Move(path, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Could not rename {path}", ex);
            ShowNotice("PicKit couldn't rename this file");
            return;
        }

        tab.Path = target;
        document.UpdateSourcePath(target);
        RememberRecent(target);
    }

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

        var image = _clipboard.ReadImage();
        if (image is null)
        {
            ShowNotice("No image on the clipboard");
            return;
        }

        await Editor.WaitUntilIdleAsync();
        App.EnsurePanelsLoaded();
        Page = null;

        var tab = TakeBlankTab() ?? AddTab(null);
        tab.Document = new EditorDocument(image, new DocumentSource(null, ImageFormat.Png, 0));
        await SelectAndShowAsync(tab);
    }

    /// <summary>A blank tab, or the one already open, instead of a second blank one.</summary>
    [RelayCommand]
    private async Task NewTabAsync()
    {
        Page = null;
        await SelectAndShowAsync(Tabs.FirstOrDefault(tab => tab.IsBlank) ?? AddTab(null));
    }

    [RelayCommand]
    private async Task CloseTabAsync(DocumentTab tab)
    {
        if (await ResolveTabAsync(tab))
        {
            if (tab.WaitingPath is { } waiting)
            {
                // Closing isn't deciding, so it waits for the next start rather than popping back.
                _closedWaitingImages.Add(waiting);
            }

            await RemoveTabAsync(tab);
        }
    }

    [RelayCommand]
    private Task CloseCurrentTabAsync() => SelectedTab is { } tab ? CloseTabAsync(tab) : Task.CompletedTask;

    [RelayCommand]
    private void NextTab()
    {
        if (Tabs.Count > 1 && SelectedTab is { } tab)
        {
            SelectedTab = Tabs[(Tabs.IndexOf(tab) + 1) % Tabs.Count];
        }
    }

    private DocumentTab? TakeBlankTab() => SelectedTab is { IsBlank: true } tab ? tab : null;

    private DocumentTab AddTab(string? path)
    {
        var tab = new DocumentTab(path);
        Tabs.Add(tab);
        OnTabsChanged();
        return tab;
    }

    private void OnTabsChanged()
    {
        OnPropertyChanged(nameof(HasTabs));
        OnPropertyChanged(nameof(HasMultipleImages));
        BatchAllCommand.NotifyCanExecuteChanged();
        RenameAllCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Gives each image a tab, reusing one if it's already open or the blank one on show.</summary>
    private async Task OpenInTabsAsync(IReadOnlyList<string> images, bool showFirst = true)
    {
        DocumentTab? first = null;
        foreach (var path in images)
        {
            var tab = Tabs.FirstOrDefault(open => string.Equals(open.Path, path, StringComparison.OrdinalIgnoreCase));
            if (tab is null && first is null && TakeBlankTab() is { } blank)
            {
                blank.Path = path;
                tab = blank;
                OnTabsChanged();
            }

            tab ??= AddTab(path);
            first ??= tab;
        }

        if (showFirst && first is not null)
        {
            await SelectAndShowAsync(first);
        }
    }

    private async Task SelectAndShowAsync(DocumentTab tab)
    {
        _isSyncingSelection = true;
        SelectedTab = tab;
        _isSyncingSelection = false;
        await ShowTabAsync(tab);
    }

    private async Task ShowTabAsync(DocumentTab tab)
    {
        await Editor.WaitUntilIdleAsync();

        // Whatever's still being typed or marked belongs to the tab being left.
        Editor.FlushPendingEdits();

        if (tab.Document is null && tab.Path is not null && !await LoadAsync(tab))
        {
            await RemoveTabAsync(tab);
            return;
        }

        // A quick second click may have moved on while this one was loading.
        if (ReferenceEquals(tab, SelectedTab))
        {
            App.EnsurePanelsLoaded();
            Editor.Document = tab.Document;
            RefreshDocumentText();
        }
    }

    private async Task RemoveTabAsync(DocumentTab tab)
    {
        var index = Tabs.IndexOf(tab);
        var wasSelected = ReferenceEquals(tab, SelectedTab);

        _isSyncingSelection = true;
        Tabs.Remove(tab);
        _isSyncingSelection = false;
        OnTabsChanged();

        if (wasSelected)
        {
            if (Tabs.Count > 0)
            {
                await SelectAndShowAsync(Tabs[Math.Min(index, Tabs.Count - 1)]);
            }
            else
            {
                _isSyncingSelection = true;
                SelectedTab = null;
                _isSyncingSelection = false;
                Editor.Document = null;
                RefreshDocumentText();
            }
        }

        tab.Document?.Dispose();
    }

    private async Task<bool> ResolveTabAsync(DocumentTab tab)
    {
        if (ReferenceEquals(tab, SelectedTab))
        {
            Editor.FlushPendingEdits();
        }

        if (!tab.IsDirty)
        {
            return true;
        }

        await SelectAndShowAsync(tab);
        return _dialogs.AskAboutUnsavedChanges(tab.Name) switch
        {
            UnsavedChoice.Save => await SaveCurrentAsync(),
            UnsavedChoice.Discard => true,
            _ => false,
        };
    }

    private Task<bool> SaveCurrentAsync() => IsWaitingImage ? KeepWaitingImageAsync() : _saver.SaveAsync(Editor);

    /// <summary>Moves a waiting image out to the save folder, along with any edits made to it.</summary>
    [RelayCommand]
    private async Task KeepWaitingAsync() => await KeepWaitingImageAsync();

    private async Task<bool> KeepWaitingImageAsync()
    {
        if (SelectedTab is not { WaitingPath: { } path } tab || Editor.Document is not { } document)
        {
            return false;
        }

        Editor.FlushPendingEdits();

        string kept;
        try
        {
            kept = _inbox.Keep(path, _background.SaveFolder, EditableName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not save a waiting image", ex);
            _dialogs.ShowError("Couldn't save", "PicKit couldn't move this image to its folder. Use Save As to choose another.", ex.Message);
            return false;
        }

        tab.WaitingPath = null;
        tab.Path = kept;
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
        if (SelectedTab is not { WaitingPath: { } path } tab)
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

        tab.WaitingPath = null;
        await RemoveTabAsync(tab);
    }

    private void OnDocumentContentChanged()
    {
        if (SelectedTab is { } tab && Editor.Document?.Source.Path is { } saved)
        {
            // Save As gave it a real file somewhere else, so the waiting copy has done its job.
            if (tab.WaitingPath is { } waiting)
            {
                SafeFile.TryDelete(waiting);
                tab.WaitingPath = null;
            }

            tab.Path = saved;
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

        Page = null;
        await OpenInTabsAsync(images);
        ShowToolFor(action);
    }

    private void RefreshRecent()
    {
        RecentImages.Clear();
        foreach (var path in _recent.Existing)
        {
            RecentImages.Add(new RecentImage(path));
        }

        OnPropertyChanged(nameof(HasRecentImages));
    }

    [RelayCommand]
    private void OpenRecent(RecentImage image) => OpenPaths([image.Path]);

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
        foreach (var tab in Tabs)
        {
            if (files.FirstOrDefault(file => file.From == tab.Path) is { } renamed)
            {
                tab.Path = renamed.To;
                tab.Document?.UpdateSourcePath(renamed.To);
                tab.Refresh();
            }
        }

        RefreshDocumentText();
    }

    partial void OnSelectedTabChanged(DocumentTab? value)
    {
        if (!_isSyncingSelection && value is not null)
        {
            _ = ShowTabAsync(value);
        }
    }

    private bool CanRunOnAllImages() => HasMultipleImages;

    [RelayCommand(CanExecute = nameof(CanRunOnAllImages))]
    private void BatchAll(BatchOperation operation) => ShowBatch(FilesInTabs, operation);

    [RelayCommand(CanExecute = nameof(CanRunOnAllImages))]
    private void RenameAll() => ShowRename(FilesInTabs);

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

    /// <summary>Loads a tab's picture. Returns false if it couldn't be opened.</summary>
    private async Task<bool> LoadAsync(DocumentTab tab)
    {
        var path = tab.Path!;
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

            tab.Document = new EditorDocument(decoded.Pixels, source);
            tab.WaitingPath = isWaiting ? path : null;
            tab.Refresh();

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

            return true;
        }
        catch (ImageException ex)
        {
            Log.Warn($"Could not open {path}", ex);
            _dialogs.ShowError("Couldn't open image", ex.Message, ex.Details);
            return false;
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
        OnPropertyChanged(nameof(EditableName));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(WaitingPath));
        OnPropertyChanged(nameof(IsWaitingImage));
        OnPropertyChanged(nameof(HasMultipleImages));
        SelectedTab?.Refresh();
        SaveCommand.NotifyCanExecuteChanged();
        SaveCopyCommand.NotifyCanExecuteChanged();
        SaveAsCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        CopyAsPngCommand.NotifyCanExecuteChanged();
        CopyAsJpegCommand.NotifyCanExecuteChanged();
    }
}
