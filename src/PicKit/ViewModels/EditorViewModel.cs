using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Controls;
using PicKit.Controls.Tools;
using PicKit.Core.Diagnostics;
using PicKit.Core.Documents;
using PicKit.Core.Imaging;
using PicKit.Services;
using PicKit.ViewModels.Tools;
using SkiaSharp;

namespace PicKit.ViewModels;

public enum EditorTab
{
    Edit,
    Tools,
    ResizeFor,
}

/// <summary>The open document, the active tool, and the undo stack the two share.</summary>
public sealed partial class EditorViewModel : ObservableObject
{
    private readonly DialogService _dialogs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDocument))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
    private EditorDocument? _document;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasTool))]
    [NotifyPropertyChangedFor(nameof(HasActiveTool))]
    private ToolViewModel? _activeTool;

    [ObservableProperty]
    private EditorTab _selectedTab = EditorTab.Edit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    [NotifyCanExecuteChangedFor(nameof(RedoCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _busyText;

    public EditorViewModel(DialogService dialogs, DocumentSaver saver)
    {
        _dialogs = dialogs;
        Crop = new CropToolViewModel(this);
        Resize = new ResizeToolViewModel(this, saver);
        Rotate = new RotateToolViewModel(this);
        Presets = new PresetToolViewModel(this);

        var colors = new ColorService();
        var red = Color.FromRgb(0xE8, 0x11, 0x23);
        Markup = new MarkupToolViewModel(
            this,
            colors,
            "Markup",
            "\uE76D",
            [
                new(MarkupMode.Pen, "Pen", "\uEDFB"),
                new(MarkupMode.Arrow, "Arrow", "\uE72A"),
                new(MarkupMode.Line, "Line", "\uE738"),
                new(MarkupMode.Rectangle, "Rectangle", "\uE739"),
                new(MarkupMode.Ellipse, "Circle", "\uEA3A"),
                new(MarkupMode.Select, "Select", "\uE7C9"),
            ],
            red,
            defaultThickness: 4,
            defaultOpacity: 1);

        Text = new MarkupToolViewModel(
            this,
            colors,
            "Text",
            "\uE8D2",
            [new(MarkupMode.Text, "Text", "\uE8D2")],
            red,
            defaultThickness: 4,
            defaultOpacity: 1,
            showsOpacity: false);

        Highlight = new MarkupToolViewModel(
            this,
            colors,
            "Highlight",
            "\uE7E6",
            [new(MarkupMode.Highlight, "Highlight", "\uE7E6")],
            Color.FromRgb(0xFF, 0xE0, 0x33),
            defaultThickness: 24,
            defaultOpacity: 0.6);

        Blur = new BlurToolViewModel(this);
        Pixelate = new PixelateToolViewModel(this);
        Redact = new RedactToolViewModel(this);

        EditTools = [Crop, Resize, Rotate, Markup, Text, Highlight, Blur, Pixelate, Redact];

        Compress = new CompressToolViewModel(this, saver, dialogs);
        Convert = new ConvertToolViewModel(this, saver, dialogs);
        Details = new DetailsToolViewModel(this);
        UtilityTools = [Compress, Convert, Details];
    }

    public event Action? DocumentContentChanged;

    /// <summary>The canvas should repaint, because a tool's unapplied work changed.</summary>
    public event Action? RedrawRequested;

    public CropToolViewModel Crop { get; }

    public ResizeToolViewModel Resize { get; }

    public RotateToolViewModel Rotate { get; }

    public PresetToolViewModel Presets { get; }

    public MarkupToolViewModel Markup { get; }

    public MarkupToolViewModel Text { get; }

    public MarkupToolViewModel Highlight { get; }

    public BlurToolViewModel Blur { get; }

    public PixelateToolViewModel Pixelate { get; }

    public RedactToolViewModel Redact { get; }

    public CompressToolViewModel Compress { get; }

    public ConvertToolViewModel Convert { get; }

    public DetailsToolViewModel Details { get; }

    public IReadOnlyList<ToolViewModel> EditTools { get; }

    public IReadOnlyList<ToolViewModel> UtilityTools { get; }

    public ICanvasTool? CanvasTool => ActiveTool?.CanvasTool;

    public bool HasDocument => Document is not null;

    public bool HasActiveTool => ActiveTool is not null;

    public bool IsIdle => !IsBusy;

    public string UndoToolTip => Document?.History.UndoLabel is { } label ? $"Undo {label} (Ctrl+Z)" : "Undo (Ctrl+Z)";

    public string RedoToolTip => Document?.History.RedoLabel is { } label ? $"Redo {label} (Ctrl+Y)" : "Redo (Ctrl+Y)";

    /// <summary>Tools call this when their canvas behaviour changes while they stay active.</summary>
    public void RefreshCanvasTool() => OnPropertyChanged(nameof(CanvasTool));

    /// <summary>Runs a pixel operation off the UI thread, then applies it as one undoable step.</summary>
    public async Task ApplyAsync(string label, Func<ImageBuffer, ImageBuffer> operation, Func<SKMatrix?>? markupMapping = null)
    {
        var document = Document;
        if (document is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyText = $"{label}...";
        try
        {
            var mapping = markupMapping?.Invoke();
            var source = document.Pixels;
            var result = await Task.Run(() => operation(source));

            if (ReferenceEquals(document, Document))
            {
                document.ReplacePixels(label, result, mapping);
            }
            else
            {
                result.Dispose();
            }
        }
        catch (ImageException ex)
        {
            Log.Warn($"{label} failed", ex);
            _dialogs.ShowError($"Couldn't finish {label.ToLowerInvariant()}", ex.Message, ex.Details);
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }

    /// <summary>Applies a privacy edit, working it out on a background thread.</summary>
    public async Task ApplyRegionAsync(string label, IReadOnlyList<SKRect> boxes, Func<ImageBuffer, SKRectI, ImageBuffer> replacement)
    {
        var document = Document;
        if (document is null || IsBusy)
        {
            return;
        }

        var regions = ToRegions(document, boxes);
        if (regions.Count == 0)
        {
            return;
        }

        IsBusy = true;
        BusyText = $"{label}...";
        try
        {
            var pixels = document.Pixels;
            var markups = document.Markups;
            var edit = await Task.Run(() => RegionEdit.Prepare(pixels, markups, regions, replacement));
            using (edit)
            {
                if (ReferenceEquals(document, Document))
                {
                    document.ApplyRegionEdit(label, edit);
                }
            }
        }
        catch (ImageException ex)
        {
            Log.Warn($"{label} failed", ex);
            _dialogs.ShowError($"Couldn't finish {label.ToLowerInvariant()}", ex.Message, ex.Details);
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }

    /// <summary>Applies a privacy edit immediately, for when an export can't proceed without it.</summary>
    public void ApplyRegionNow(string label, IReadOnlyList<SKRectI> regions, Func<ImageBuffer, SKRectI, ImageBuffer> replacement)
    {
        if (Document is { } document)
        {
            using var edit = RegionEdit.Prepare(document.Pixels, document.Markups, regions, replacement);
            document.ApplyRegionEdit(label, edit);
        }
    }

    /// <summary>Makes sure anything still marked on screen is really in the image.</summary>
    public void FlushPendingEdits() => ActiveTool?.FlushPending();

    public void RedrawCanvas() => RedrawRequested?.Invoke();

    public void BeginBusy(string text)
    {
        IsBusy = true;
        BusyText = text;
    }

    public void EndBusy()
    {
        IsBusy = false;
        BusyText = null;
    }

    /// <summary>Waits for background work, so nothing is reading an image that's about to be closed.</summary>
    public async Task WaitUntilIdleAsync()
    {
        while (IsBusy)
        {
            await Task.Delay(20);
        }
    }

    public void NotifySaved() => DocumentContentChanged?.Invoke();

    public void ClearActiveTool()
    {
        ActiveTool = null;
    }

    partial void OnDocumentChanged(EditorDocument? oldValue, EditorDocument? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.Changed -= OnDocumentContentChanged;
        }

        if (newValue is not null)
        {
            newValue.Changed += OnDocumentContentChanged;
        }

        ActiveTool = null;
        NotifyHistory();
    }

    partial void OnActiveToolChanged(ToolViewModel? oldValue, ToolViewModel? newValue)
    {
        oldValue?.Deactivate();
        newValue?.Activate();
    }

    partial void OnSelectedTabChanged(EditorTab value)
    {
        ActiveTool = value == EditorTab.ResizeFor && HasDocument ? Presets : null;
    }

    private void OnDocumentContentChanged(DocumentChange change)
    {
        NotifyHistory();
        if (change.Kind != DocumentChangeKind.Markup)
        {
            ActiveTool?.OnImageChanged();
        }

        DocumentContentChanged?.Invoke();
    }

    private static List<SKRectI> ToRegions(EditorDocument document, IReadOnlyList<SKRect> boxes) =>
        boxes.Select(box => ImageOps.CoveringPixels(box, document.Pixels.Width, document.Pixels.Height))
            .Where(region => !region.IsEmpty)
            .ToList();

    private void NotifyHistory()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(UndoToolTip));
        OnPropertyChanged(nameof(RedoToolTip));
    }

    private bool CanUndo() => IsIdle && Document?.History.CanUndo == true;

    private bool CanRedo() => IsIdle && Document?.History.CanRedo == true;

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => Document?.Undo();

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => Document?.Redo();

    /// <summary>Escape: backs out of whatever is in progress, then out of the tool.</summary>
    [RelayCommand]
    private void Cancel()
    {
        if (IsIdle && ActiveTool?.CancelPending() != true)
        {
            ClearActiveTool();
        }
    }

    [RelayCommand]
    private void DeleteSelection()
    {
        if (IsIdle && ActiveTool is MarkupToolViewModel markup)
        {
            markup.DeleteSelected();
        }
    }

    [RelayCommand]
    private void ApplyPending()
    {
        if (IsIdle)
        {
            ActiveTool?.ApplyPending();
        }
    }
}
