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

    public EditorViewModel(DialogService dialogs)
    {
        _dialogs = dialogs;
        Crop = new CropToolViewModel(this);
        Resize = new ResizeToolViewModel(this);
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

        EditTools = [Crop, Resize, Rotate, Markup, Text, Highlight];
    }

    public event Action? DocumentContentChanged;

    public CropToolViewModel Crop { get; }

    public ResizeToolViewModel Resize { get; }

    public RotateToolViewModel Rotate { get; }

    public PresetToolViewModel Presets { get; }

    public MarkupToolViewModel Markup { get; }

    public MarkupToolViewModel Text { get; }

    public MarkupToolViewModel Highlight { get; }

    public IReadOnlyList<ToolViewModel> EditTools { get; }

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
        if (IsIdle)
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
