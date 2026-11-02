using PicKit.Core.Imaging;
using PicKit.Core.Markup;
using SkiaSharp;

namespace PicKit.Background;

public enum SnipButton
{
    None,
    FullScreen,
    Cancel,
}

/// <summary>Where things go on the snipping screen, in the overlay's own pixels.</summary>
public sealed class SnipLayout
{
    private const int BarWidthDip = 300;
    private const int BarHeightDip = 44;
    private const int BarTopDip = 16;
    private const int ButtonGapDip = 6;
    private const int FullScreenWidthDip = 96;
    private const int CancelWidthDip = 70;

    /// <summary>A drag smaller than this was a slip of the mouse, not a selection.</summary>
    private const int MinimumSelection = 4;

    public SnipLayout(SKRectI bounds, SKRectI monitor, float scale)
    {
        Bounds = bounds;
        Monitor = monitor;
        Scale = scale;

        int Px(int dip) => (int)(dip * scale);
        var left = monitor.Left + ((monitor.Width - Px(BarWidthDip)) / 2);
        var top = monitor.Top + Px(BarTopDip);
        Bar = new SKRectI(left, top, left + Px(BarWidthDip), top + Px(BarHeightDip));

        var inset = Px(ButtonGapDip);
        Cancel = new SKRectI(Bar.Right - inset - Px(CancelWidthDip), Bar.Top + inset, Bar.Right - inset, Bar.Bottom - inset);
        FullScreen = new SKRectI(Cancel.Left - inset - Px(FullScreenWidthDip), Cancel.Top, Cancel.Left - inset, Cancel.Bottom);
    }

    /// <summary>The whole overlay, which covers every monitor.</summary>
    public SKRectI Bounds { get; }

    /// <summary>The monitor the snip began on, which is what Full screen captures.</summary>
    public SKRectI Monitor { get; }

    public float Scale { get; }

    public SKRectI Bar { get; }

    public SKRectI FullScreen { get; }

    public SKRectI Cancel { get; }

    public static bool IsUsable(SKRectI selection) =>
        selection.Width >= MinimumSelection && selection.Height >= MinimumSelection;

    /// <summary>The dragged area, whichever way it was dragged, kept on the screen.</summary>
    public SKRectI Selection(SKPointI start, SKPointI current) => SKRectI.Intersect(
        new SKRectI(Math.Min(start.X, current.X), Math.Min(start.Y, current.Y), Math.Max(start.X, current.X), Math.Max(start.Y, current.Y)),
        Bounds);

    public bool IsOnBar(SKPointI point) => Bar.Contains(point);

    public SnipButton ButtonAt(SKPointI point) =>
        FullScreen.Contains(point) ? SnipButton.FullScreen : Cancel.Contains(point) ? SnipButton.Cancel : SnipButton.None;
}

/// <summary>
/// The snipping screen, like Snip &amp; Sketch: the screen freezes and dims, and dragging picks the
/// part to keep. The picture was taken before this appeared, so none of it is in the result.
/// It never takes focus, so the app in use isn't told it lost it.
/// </summary>
internal sealed class SnipOverlay : AgentWindow
{
    public const string Title = "PicKit snip";

    private const int EscapeHotkey = 11;
    private const int EnterHotkey = 12;
    private const uint EscapeKey = 0x1B;
    private const uint EnterKey = 0x0D;
    private const byte DimAlpha = 120;

    private readonly Action<ImageBuffer> _finished;
    private readonly Action _cancelled;
    private FrozenScreen? _screen;
    private SnipLayout? _layout;
    private ImageBuffer? _dimmed;
    private SKImage? _brightImage;
    private SKImage? _dimImage;
    private LayeredSurface? _frame;
    private SKPointI _start;
    private SKPointI _current;
    private bool _isSelecting;
    private SnipButton _hot;

    public SnipOverlay(Action<ImageBuffer> finished, Action cancelled)
        : base(Native.WS_POPUP, Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST, Title)
    {
        _finished = finished;
        _cancelled = cancelled;
    }

    public bool IsOpen => _screen is not null;

    public void Begin(FrozenScreen screen)
    {
        _screen = screen;
        var image = screen.Image;
        _layout = new SnipLayout(new SKRectI(0, 0, image.Width, image.Height), screen.Monitor, screen.Scale);

        _dimmed = Dimmed(image);
        _brightImage = image.AsImage();
        _dimImage = _dimmed.AsImage();
        _frame = new LayeredSurface(image.Width, image.Height);
        _isSelecting = false;
        _hot = SnipButton.None;
        Render();

        // Paints fully before it shows, so there's no flash of an empty window.
        Native.SetWindowPos(
            Handle,
            Native.HWND_TOPMOST,
            screen.Origin.X,
            screen.Origin.Y,
            image.Width,
            image.Height,
            Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);

        Native.RegisterHotKey(Handle, EscapeHotkey, Native.MOD_NOREPEAT, EscapeKey);
        Native.RegisterHotKey(Handle, EnterHotkey, Native.MOD_NOREPEAT, EnterKey);
    }

    public void Cancel()
    {
        if (IsOpen)
        {
            Close();
            _cancelled();
        }
    }

    public void TakeFullScreen()
    {
        if (_layout is { } layout)
        {
            Finish(SKRectI.Intersect(layout.Monitor, layout.Bounds));
        }
    }

    public override void Dispose()
    {
        Close();
        base.Dispose();
    }

    /// <summary>A darker copy of the picture. The caller owns the result.</summary>
    internal static ImageBuffer Dimmed(ImageBuffer image)
    {
        var dimmed = image.Clone();
        using var canvas = dimmed.CreateCanvas();

        // Left to its default, DrawColor replaces the pixels instead of darkening them.
        canvas.DrawColor(new SKColor(0, 0, 0, DimAlpha), SKBlendMode.SrcOver);
        return dimmed;
    }

    /// <summary>Draws one frame. Separate from the window so it can be checked as a picture.</summary>
    internal static void Paint(SKCanvas canvas, SnipLayout layout, SKImage bright, SKImage dimmed, SKRectI? selection, SnipButton hot)
    {
        canvas.DrawImage(dimmed, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null);
        if (selection is { } area)
        {
            var rectangle = new SKRect(area.Left, area.Top, area.Right, area.Bottom);
            canvas.DrawImage(bright, rectangle, rectangle, new SKSamplingOptions(SKFilterMode.Nearest));

            using var edge = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = Math.Max(1, layout.Scale) };
            canvas.DrawRect(rectangle, edge);
            DrawSizeLabel(canvas, layout, area);
        }

        DrawBar(canvas, layout, hot);
    }

    protected override IntPtr OnMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case Native.WM_MOUSEACTIVATE:
                return Native.MA_NOACTIVATE;
            case Native.WM_ERASEBKGND:
                return 1;
            case Native.WM_PAINT:
                OnPaint();
                return 0;
            case Native.WM_SETCURSOR:
                OnSetCursor();
                return 1;
            case Native.WM_LBUTTONDOWN:
                OnPress(PointFrom(lParam));
                return 0;
            case Native.WM_MOUSEMOVE:
                OnMove(PointFrom(lParam));
                return 0;
            case Native.WM_LBUTTONUP:
                OnRelease(PointFrom(lParam));
                return 0;
            case Native.WM_RBUTTONUP:
                Cancel();
                return 0;
            case Native.WM_HOTKEY when wParam == EscapeHotkey:
                Cancel();
                return 0;
            case Native.WM_HOTKEY when wParam == EnterHotkey:
                TakeFullScreen();
                return 0;
            default:
                return base.OnMessage(message, wParam, lParam);
        }
    }

    private static SKPointI PointFrom(IntPtr lParam) =>
        new((short)(lParam.ToInt64() & 0xFFFF), (short)((lParam.ToInt64() >> 16) & 0xFFFF));

    private static void DrawBar(SKCanvas canvas, SnipLayout layout, SnipButton hot)
    {
        var scale = layout.Scale;
        var bar = new SKRect(layout.Bar.Left, layout.Bar.Top, layout.Bar.Right, layout.Bar.Bottom);
        using var fill = new SKPaint { Color = new SKColor(0x2B, 0x2B, 0x2B), IsAntialias = true };
        using var border = new SKPaint { Color = new SKColor(0x55, 0x55, 0x55), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = scale };
        canvas.DrawRoundRect(bar, 8 * scale, 8 * scale, fill);
        canvas.DrawRoundRect(bar, 8 * scale, 8 * scale, border);

        using var font = new SKFont(TextFonts.Typeface(TextFonts.Default), 13 * scale);
        using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var baseline = bar.MidY + (font.Size * 0.35f);
        canvas.DrawText("Drag to snip", bar.Left + (14 * scale), baseline, SKTextAlign.Left, font, ink);

        DrawButton(canvas, layout.FullScreen, "Full screen", hot == SnipButton.FullScreen, scale, font, ink);
        DrawButton(canvas, layout.Cancel, "Cancel", hot == SnipButton.Cancel, scale, font, ink);
    }

    private static void DrawButton(SKCanvas canvas, SKRectI box, string text, bool isHot, float scale, SKFont font, SKPaint ink)
    {
        var rectangle = new SKRect(box.Left, box.Top, box.Right, box.Bottom);
        using var fill = new SKPaint { Color = isHot ? new SKColor(0x00, 0x78, 0xD4) : new SKColor(0x44, 0x44, 0x44), IsAntialias = true };
        canvas.DrawRoundRect(rectangle, 5 * scale, 5 * scale, fill);
        canvas.DrawText(text, rectangle.MidX, rectangle.MidY + (font.Size * 0.35f), SKTextAlign.Center, font, ink);
    }

    private static void DrawSizeLabel(SKCanvas canvas, SnipLayout layout, SKRectI area)
    {
        var scale = layout.Scale;
        using var font = new SKFont(TextFonts.Typeface(TextFonts.Default), 12 * scale);
        using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var back = new SKPaint { Color = new SKColor(0, 0, 0, 180), IsAntialias = true };

        var text = $"{area.Width} × {area.Height}";
        var width = font.MeasureText(text) + (12 * scale);
        var height = 22 * scale;
        var top = area.Bottom + (6 * scale) + height < layout.Bounds.Bottom ? area.Bottom + (6 * scale) : area.Bottom - height - (6 * scale);
        var box = SKRect.Create(area.Left, top, width, height);
        canvas.DrawRoundRect(box, 4 * scale, 4 * scale, back);
        canvas.DrawText(text, box.Left + (6 * scale), box.MidY + (font.Size * 0.35f), SKTextAlign.Left, font, ink);
    }

    private void OnPaint()
    {
        var dc = Native.BeginPaint(Handle, out var paint);
        _frame?.CopyTo(dc);
        Native.EndPaint(Handle, ref paint);
    }

    private void OnSetCursor()
    {
        var overBar = Native.GetCursorPos(out var cursor)
            && _layout is { } layout
            && _screen is { } screen
            && layout.IsOnBar(new SKPointI(cursor.X - screen.Origin.X, cursor.Y - screen.Origin.Y));

        Native.SetCursor(Native.LoadCursorW(0, overBar ? Native.IDC_ARROW : Native.IDC_CROSS));
    }

    private void OnPress(SKPointI point)
    {
        if (_layout is not { } layout)
        {
            return;
        }

        switch (layout.ButtonAt(point))
        {
            case SnipButton.FullScreen:
                TakeFullScreen();
                return;
            case SnipButton.Cancel:
                Cancel();
                return;
        }

        if (!layout.IsOnBar(point))
        {
            _start = point;
            _current = point;
            _isSelecting = true;
            Native.SetCapture(Handle);
        }
    }

    private void OnMove(SKPointI point)
    {
        if (_layout is not { } layout)
        {
            return;
        }

        var hot = _isSelecting ? SnipButton.None : layout.ButtonAt(point);
        if (_isSelecting || hot != _hot)
        {
            _current = point;
            _hot = hot;
            Render();
            Native.InvalidateRect(Handle, 0, false);
        }
    }

    private void OnRelease(SKPointI point)
    {
        if (!_isSelecting || _layout is not { } layout)
        {
            return;
        }

        _isSelecting = false;
        Native.ReleaseCapture();

        var selection = layout.Selection(_start, point);
        if (SnipLayout.IsUsable(selection))
        {
            Finish(selection);
        }
        else
        {
            Render();
            Native.InvalidateRect(Handle, 0, false);
        }
    }

    private void Render()
    {
        if (_frame is { } frame && _layout is { } layout && _brightImage is { } bright && _dimImage is { } dimmed)
        {
            var selection = _isSelecting ? layout.Selection(_start, _current) : (SKRectI?)null;
            frame.Paint(canvas => Paint(canvas, layout, bright, dimmed, selection, _hot));
        }
    }

    private void Finish(SKRectI region)
    {
        if (_screen is not { } screen)
        {
            return;
        }

        var snip = PixelOps.CopyRegion(screen.Image, region);
        Close();
        _finished(snip);
    }

    private void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        Native.UnregisterHotKey(Handle, EscapeHotkey);
        Native.UnregisterHotKey(Handle, EnterHotkey);
        Native.ReleaseCapture();
        Native.ShowWindow(Handle, Native.SW_HIDE);

        _brightImage?.Dispose();
        _dimImage?.Dispose();
        _dimmed?.Dispose();
        _frame?.Dispose();
        _screen?.Image.Dispose();
        (_brightImage, _dimImage, _dimmed, _frame, _screen, _layout) = (null, null, null, null, null, null);
    }
}
