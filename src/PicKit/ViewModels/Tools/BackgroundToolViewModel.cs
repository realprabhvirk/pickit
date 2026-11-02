using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Controls;
using PicKit.Controls.Tools;
using PicKit.Core.Diagnostics;
using PicKit.Core.Imaging;
using PicKit.Core.Segmentation;

namespace PicKit.ViewModels.Tools;

public enum BackgroundToolState
{
    Idle,
    Processing,
    Ready,
    ModelMissing,
    Failed,
}

/// <summary>
/// Remove Background: the model makes a first attempt, and the brushes fix what it got wrong.
/// The model is only loaded while it runs, and its memory goes back as soon as the mask exists.
/// </summary>
public sealed partial class BackgroundToolViewModel : ToolViewModel, IToolHistory
{
    private const float ReferenceSide = 1000;

    private readonly SegmentationModel _model;
    private MaskEditor? _mask;
    private MaskBrushCanvasTool? _canvasTool;
    private CancellationTokenSource? _running;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorking))]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private BackgroundToolState _state;

    [ObservableProperty]
    private BrushMode _brushMode = BrushMode.Remove;

    [ObservableProperty]
    private double _brushSize = 30;

    public BackgroundToolViewModel(EditorViewModel editor, SegmentationModel? model = null)
        : base(editor)
    {
        _model = model ?? SegmentationModel.Default;
    }

    public override string Name => "Remove BG";

    public override string Glyph => "";

    public override ICanvasTool? CanvasTool => _canvasTool;

    public override IToolHistory? History => _mask is null ? null : this;

    public bool IsWorking => State == BackgroundToolState.Processing;

    public bool CanEdit => _mask is not null && !IsWorking;

    public string StatusText => State switch
    {
        BackgroundToolState.Processing => "Removing background...",
        BackgroundToolState.Ready => "Checks show what's been removed. Paint to fix anything it got wrong.",
        BackgroundToolState.ModelMissing => "Automatic removal isn't installed with this copy of PicKit. You can still paint out the background by hand.",
        BackgroundToolState.Failed => "PicKit couldn't work out the background of this image. You can still paint it out by hand.",
        _ => string.Empty,
    };

    public bool CanUndo => _mask?.CanUndo == true;

    public bool CanRedo => _mask?.CanRedo == true;

    private float BrushRadius
    {
        get
        {
            var pixels = Editor.Document?.Pixels;
            var unit = pixels is null ? 1 : Math.Max(1, Math.Max(pixels.Width, pixels.Height) / ReferenceSide);
            return (float)BrushSize * unit;
        }
    }

    public override void Activate() => _ = StartAsync();

    public override void Deactivate()
    {
        _running?.Cancel();
        ApplyMask();
        CloseSession();
    }

    /// <summary>Saving or copying uses what's on screen, which here is the masked image.</summary>
    public override void FlushPending()
    {
        ApplyMask();
        CloseSession();
        Editor.RefreshCanvasTool();
    }

    public override bool ApplyPending()
    {
        if (CanEdit)
        {
            ApplyCommand.Execute(null);
        }

        return true;
    }

    /// <summary>Escape abandons the session, leaving the image as it was.</summary>
    public override bool CancelPending()
    {
        _running?.Cancel();
        CloseSession();
        return false;
    }

    public void Undo()
    {
        _mask?.Undo();
        Editor.NotifyToolHistoryChanged();
    }

    public void Redo()
    {
        _mask?.Redo();
        Editor.NotifyToolHistoryChanged();
    }

    private async Task StartAsync()
    {
        if (Editor.Document is not { } document)
        {
            return;
        }

        CloseSession();
        var path = _model.Locate();
        if (path is null)
        {
            OpenSession(MaskEditor.KeepEverything(document.Pixels.Width, document.Pixels.Height));
            State = BackgroundToolState.ModelMissing;
            return;
        }

        State = BackgroundToolState.Processing;
        var running = new CancellationTokenSource();
        _running = running;

        // A private copy, so the image can change or close while this runs.
        var snapshot = document.RenderFlattened();
        try
        {
            var mask = await Task.Run(
                () =>
                {
                    using var segmenter = new BackgroundSegmenter(_model, path);
                    return segmenter.CreateMask(snapshot, running.Token);
                },
                running.Token);

            if (running.IsCancellationRequested || !ReferenceEquals(document, Editor.Document))
            {
                mask.Dispose();
                return;
            }

            OpenSession(mask);
            State = BackgroundToolState.Ready;
        }
        catch (OperationCanceledException)
        {
            State = BackgroundToolState.Idle;
        }
        catch (Exception ex) when (ex is ImageException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException or DllNotFoundException)
        {
            Log.Warn("Background removal failed", ex);
            OpenSession(MaskEditor.KeepEverything(document.Pixels.Width, document.Pixels.Height));
            State = BackgroundToolState.Failed;
        }
        finally
        {
            snapshot.Dispose();
            if (ReferenceEquals(_running, running))
            {
                _running = null;
            }
        }
    }

    private void OpenSession(ImageBuffer mask)
    {
        _mask = new MaskEditor(mask);
        _mask.Changed += OnMaskChanged;
        _canvasTool = new MaskBrushCanvasTool(_mask, () => BrushMode, () => BrushRadius);
        OnPropertyChanged(nameof(CanEdit));
        Editor.RefreshCanvasTool();
    }

    private void CloseSession()
    {
        var tool = _canvasTool;
        var mask = _mask;
        _canvasTool = null;
        _mask = null;

        if (mask is not null)
        {
            mask.Changed -= OnMaskChanged;
        }

        State = BackgroundToolState.Idle;
        Editor.RefreshCanvasTool();
        tool?.Dispose();
        mask?.Dispose();
    }

    private void OnMaskChanged(SkiaSharp.SKRectI region)
    {
        Editor.RedrawCanvas();
        Editor.NotifyToolHistoryChanged();
        RestoreCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Burns the mask into the image as one undoable edit, if it changed anything.</summary>
    private void ApplyMask()
    {
        if (Editor.Document is not { } document || _mask is not { } editor)
        {
            return;
        }

        var keepsEverything = true;
        foreach (var pixel in editor.Mask.Pixels)
        {
            if (pixel >> 24 != 255)
            {
                keepsEverything = false;
                break;
            }
        }

        if (keepsEverything)
        {
            return;
        }

        using var flattened = document.RenderFlattened();
        document.BakeAndReplace(Name == "Remove BG" ? "Remove background" : Name, ImageOps.ApplyMask(flattened, editor.Mask));
    }

    private bool CanFinish() => CanEdit;

    [RelayCommand(CanExecute = nameof(CanFinish))]
    private void Apply()
    {
        ApplyMask();
        CloseSession();
        Editor.ClearActiveTool();
    }

    [RelayCommand]
    private void Cancel()
    {
        _running?.Cancel();
        CloseSession();
        Editor.ClearActiveTool();
    }

    [RelayCommand(CanExecute = nameof(CanFinish))]
    private void Restore() => _mask?.Restore();
}
