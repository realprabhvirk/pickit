using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Controls;
using PicKit.Controls.Tools;
using PicKit.Core.Imaging;
using SkiaSharp;

namespace PicKit.ViewModels.Tools;

/// <summary>
/// Redact: mark areas, then replace their pixels. What's on screen is what gets exported: leaving
/// the tool, saving or copying applies anything still marked. A black box the user can see must
/// never turn out to be missing from the file.
/// </summary>
public sealed partial class RedactToolViewModel : ToolViewModel
{
    private readonly RegionCanvasTool _canvasTool;

    [ObservableProperty]
    private bool _useWhite;

    public RedactToolViewModel(EditorViewModel editor)
        : base(editor)
    {
        _canvasTool = new RegionCanvasTool(AddRegion, DrawPending);
        Pending.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasPending));
            OnPropertyChanged(nameof(PendingText));
            ApplyCommand.NotifyCanExecuteChanged();
            ClearCommand.NotifyCanExecuteChanged();
        };
    }

    public override string Name => "Redact";

    public override string Glyph => "";

    public override ICanvasTool CanvasTool => _canvasTool;

    public ObservableCollection<SKRect> Pending { get; } = [];

    public bool HasPending => Pending.Count > 0;

    public string PendingText => Pending.Count switch
    {
        0 => "Drag over anything you want to remove.",
        1 => "1 area marked.",
        _ => $"{Pending.Count} areas marked.",
    };

    private SKColor FillColor => UseWhite ? SKColors.White : SKColors.Black;

    public override void Deactivate() => ApplyPending();

    public override void OnImageChanged()
    {
        if (!_isApplying)
        {
            Pending.Clear();
        }
    }

    public override bool ApplyPending()
    {
        if (!HasPending)
        {
            return false;
        }

        ApplyNow();
        return true;
    }

    /// <summary>Applies marked areas synchronously, for use just before an export.</summary>
    public override void FlushPending() => ApplyPending();

    /// <summary>Escape clears marks first, and only then leaves the tool.</summary>
    public override bool CancelPending()
    {
        if (!HasPending)
        {
            return false;
        }

        Pending.Clear();
        Editor.RedrawCanvas();
        return true;
    }

    private bool _isApplying;

    private void AddRegion(SKRect box) => Pending.Add(box);

    private void DrawPending(SKCanvas canvas)
    {
        using var paint = new SKPaint { Color = FillColor };
        foreach (var box in Pending)
        {
            canvas.DrawRect(box, paint);
        }
    }

    private void ApplyNow()
    {
        if (Editor.Document is not { } document)
        {
            Pending.Clear();
            return;
        }

        var regions = Pending
            .Select(box => ImageOps.CoveringPixels(box, document.Pixels.Width, document.Pixels.Height))
            .Where(region => !region.IsEmpty)
            .ToList();

        _isApplying = true;
        try
        {
            if (regions.Count > 0)
            {
                var color = FillColor;
                Editor.ApplyRegionNow("Redact", regions, (_, region) => ImageOps.SolidRegion(region.Width, region.Height, color));
            }

            Pending.Clear();
        }
        finally
        {
            _isApplying = false;
        }
    }

    private bool CanApply() => HasPending;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply() => ApplyNow();

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Clear() => CancelPending();
}
