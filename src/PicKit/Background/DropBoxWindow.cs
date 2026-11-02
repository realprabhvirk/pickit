using System.Runtime.InteropServices;
using PicKit.Core.Background;
using PicKit.Core.Diagnostics;
using SkiaSharp;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace PicKit.Background;

/// <summary>The Windows interface that lets a window accept drags from other apps.</summary>
[ComImport]
[Guid("00000122-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDropTarget
{
    [PreserveSig]
    int DragEnter([MarshalAs(UnmanagedType.Interface)] ComDataObject data, int keyState, long point, ref int effect);

    [PreserveSig]
    int DragOver(int keyState, long point, ref int effect);

    [PreserveSig]
    int DragLeave();

    [PreserveSig]
    int Drop([MarshalAs(UnmanagedType.Interface)] ComDataObject data, int keyState, long point, ref int effect);
}

/// <summary>Accepts a drag and reports it. Whatever went wrong, the other app must never crash.</summary>
[ComVisible(true)]
public sealed class DropTarget : IDropTarget
{
    private const int DropEffectCopy = 1;
    private const int DropEffectLink = 4;

    private readonly Action<bool> _highlight;
    private readonly Action<DroppedItems> _dropped;

    public DropTarget(Action<bool> highlight, Action<DroppedItems> dropped)
    {
        _highlight = highlight;
        _dropped = dropped;
    }

    public int DragEnter(ComDataObject data, int keyState, long point, ref int effect)
    {
        effect = Accept(effect);
        _highlight(true);
        return 0;
    }

    public int DragOver(int keyState, long point, ref int effect)
    {
        effect = Accept(effect);
        return 0;
    }

    public int DragLeave()
    {
        _highlight(false);
        return 0;
    }

    public int Drop(ComDataObject data, int keyState, long point, ref int effect)
    {
        effect = Accept(effect);
        _highlight(false);
        try
        {
            _dropped(DropReader.Read(data));
        }
        catch (Exception ex)
        {
            Log.Error("Could not take what was dropped", ex);
        }

        return 0;
    }

    /// <summary>Browsers often offer a link rather than a copy, so either is fine.</summary>
    private static int Accept(int allowed) => (allowed & DropEffectCopy) != 0 ? DropEffectCopy : allowed & DropEffectLink;
}

/// <summary>
/// The tiny box at the edge of the screen. Dragging an image onto it opens it in PicKit. It can
/// be dragged anywhere, and it stays out of screenshots so it never ends up in one.
/// </summary>
internal sealed class DropBoxWindow : AgentWindow
{
    public const int SizeDip = 44;

    private const int EdgeGapDip = 6;
    private const int DragThreshold = 4;
    private const double DefaultHeightShare = 0.6;
    private const int MenuOpen = 1;
    private const int MenuHide = 2;
    private const string Glyph = "";

    private readonly BackgroundSettingsStore _settings;
    private readonly Action _open;
    private readonly DropTarget _target;
    private LayeredSurface? _surface;
    private int _x;
    private int _y;
    private bool _isPressed;
    private bool _hasDragged;
    private bool _isHighlighted;
    private Native.POINT _pressPoint;
    private Native.POINT _pressOrigin;

    public DropBoxWindow(BackgroundSettingsStore settings, Action open, Action<DroppedItems> dropped)
        : base(Native.WS_POPUP, Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST)
    {
        _settings = settings;
        _open = open;
        _target = new DropTarget(SetHighlight, dropped);

        Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE);
        var result = Native.RegisterDragDrop(Handle, _target);
        if (result != 0)
        {
            Log.Warn($"The drop box could not accept drags: 0x{result:X8}");
        }

        PlaceOnScreen();
        Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
    }

    /// <summary>Monitors come and go, and a saved spot can end up off every screen.</summary>
    public void PlaceOnScreen()
    {
        var saved = _settings.Load();
        var point = new Native.POINT { X = saved.DropBoxLeft ?? 0, Y = saved.DropBoxTop ?? 0 };
        var monitor = saved.DropBoxLeft is null ? IntPtr.Zero : Native.MonitorFromPoint(point, Native.MONITOR_DEFAULTTONULL);

        if (monitor == IntPtr.Zero)
        {
            monitor = Native.MonitorFromPoint(default, Native.MONITOR_DEFAULTTONEAREST);
            var work = InfoFor(monitor).rcWork;
            var scale = ScaleFor(monitor);
            point.X = work.Right - (int)((SizeDip + EdgeGapDip) * scale);
            point.Y = work.Top + (int)(work.Height * DefaultHeightShare);
        }

        _x = point.X;
        _y = point.Y;
        Repaint(ScaleFor(monitor));
    }

    public override void Dispose()
    {
        Native.RevokeDragDrop(Handle);
        _surface?.Dispose();
        base.Dispose();
    }

    /// <summary>Draws the box. Separate from the window so it can be checked as a picture.</summary>
    internal static void Paint(SKCanvas canvas, int size, float scale, bool isHighlighted)
    {
        var box = new SKRect(scale, scale, size - scale, size - scale);
        var rounded = new SKRoundRect(box, 10 * scale);
        var background = isHighlighted ? new SKColor(0x00, 0x78, 0xD4) : new SKColor(0x2B, 0x2B, 0x2B, 0xE6);
        using var fill = new SKPaint { Color = background, IsAntialias = true };
        using var edge = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF, 0x50), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = scale };
        canvas.DrawRoundRect(rounded, fill);
        canvas.DrawRoundRect(rounded, edge);

        using var typeface = SKTypeface.FromFamilyName("Segoe Fluent Icons") ?? SKTypeface.FromFamilyName("Segoe MDL2 Assets");
        using var font = new SKFont(typeface, 22 * scale);
        using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var bounds = font.MeasureText(Glyph, out var glyph);
        canvas.DrawText(Glyph, (size - bounds) / 2, ((size - glyph.Height) / 2) - glyph.Top, SKTextAlign.Left, font, ink);
    }

    protected override IntPtr OnMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case Native.WM_MOUSEACTIVATE:
                return Native.MA_NOACTIVATE;
            case Native.WM_LBUTTONDOWN:
                OnPress();
                return 0;
            case Native.WM_MOUSEMOVE:
                OnMove();
                return 0;
            case Native.WM_LBUTTONUP:
                OnRelease();
                return 0;
            case Native.WM_RBUTTONUP:
                ShowMenu();
                return 0;
            default:
                return base.OnMessage(message, wParam, lParam);
        }
    }

    private void SetHighlight(bool isHighlighted)
    {
        _isHighlighted = isHighlighted;
        Repaint(CurrentScale());
    }

    private float CurrentScale()
    {
        var point = new Native.POINT { X = _x, Y = _y };
        return ScaleFor(Native.MonitorFromPoint(point, Native.MONITOR_DEFAULTTONEAREST));
    }

    private void Repaint(float scale)
    {
        var size = (int)(SizeDip * scale);
        if (_surface?.Width != size)
        {
            _surface?.Dispose();
            _surface = new LayeredSurface(size, size);
        }

        var highlighted = _isHighlighted;
        _surface.Paint(canvas => Paint(canvas, size, scale, highlighted));
        _surface.Present(Handle, _x, _y);
    }

    private void OnPress()
    {
        Native.GetCursorPos(out _pressPoint);
        _pressOrigin = new Native.POINT { X = _x, Y = _y };
        _isPressed = true;
        _hasDragged = false;
        Native.SetCapture(Handle);
    }

    private void OnMove()
    {
        if (!_isPressed)
        {
            return;
        }

        Native.GetCursorPos(out var cursor);
        var deltaX = cursor.X - _pressPoint.X;
        var deltaY = cursor.Y - _pressPoint.Y;
        _hasDragged |= Math.Abs(deltaX) > DragThreshold || Math.Abs(deltaY) > DragThreshold;
        if (_hasDragged)
        {
            _x = _pressOrigin.X + deltaX;
            _y = _pressOrigin.Y + deltaY;
            _surface?.Present(Handle, _x, _y);
        }
    }

    private void OnRelease()
    {
        if (!_isPressed)
        {
            return;
        }

        _isPressed = false;
        Native.ReleaseCapture();
        if (_hasDragged)
        {
            SnapToEdge();
        }
        else
        {
            _open();
        }
    }

    /// <summary>Settles against the nearer side of the screen, so it never sits over the work.</summary>
    private void SnapToEdge()
    {
        var point = new Native.POINT { X = _x, Y = _y };
        var monitor = Native.MonitorFromPoint(point, Native.MONITOR_DEFAULTTONEAREST);
        var work = InfoFor(monitor).rcWork;
        var scale = ScaleFor(monitor);
        var size = (int)(SizeDip * scale);
        var gap = (int)(EdgeGapDip * scale);

        _x = _x + (size / 2) < work.Left + (work.Width / 2) ? work.Left + gap : work.Right - size - gap;
        _y = Math.Clamp(_y, work.Top + gap, work.Bottom - size - gap);
        Repaint(scale);

        var (x, y) = (_x, _y);
        _settings.Update(settings =>
        {
            settings.DropBoxLeft = x;
            settings.DropBoxTop = y;
        });
    }

    private void ShowMenu()
    {
        var menu = Native.CreatePopupMenu();
        Native.AppendMenuW(menu, Native.MF_STRING, MenuOpen, "Open PicKit");
        Native.AppendMenuW(menu, Native.MF_STRING, MenuHide, "Hide this box");

        // A menu only closes on an outside click if its window is in the foreground.
        Native.SetForegroundWindow(Handle);
        Native.GetCursorPos(out var cursor);
        var choice = Native.TrackPopupMenu(menu, Native.TPM_RETURNCMD | Native.TPM_NONOTIFY, cursor.X, cursor.Y, 0, Handle, 0);
        Native.DestroyMenu(menu);

        if (choice == MenuOpen)
        {
            _open();
        }
        else if (choice == MenuHide)
        {
            _settings.Update(settings => settings.ShowDropBox = false);
            Native.ShowWindow(Handle, Native.SW_HIDE);
        }
    }
}
