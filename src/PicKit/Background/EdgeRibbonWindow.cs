using System.Runtime.InteropServices;
using System.Text;
using PicKit.Core.Background;
using PicKit.Core.Diagnostics;
using PicKit.Core.Markup;
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

public enum RibbonPart
{
    None,
    Snip,
    Open,
}

/// <summary>
/// Where the ribbon goes. The window always has the open panel's size, and the strip is a part
/// of it, so a drag that reaches the strip carries straight on into the panel.
/// </summary>
public sealed class RibbonLayout
{
    private const int StripReachDip = 10;
    private const int StripHeightDip = 64;
    private const int GripWidthDip = 4;
    private const int GripHeightDip = 44;
    private const int PanelWidthDip = 204;
    private const int PanelHeightDip = 150;
    private const int ScreenMarginDip = 8;
    private const int InsetDip = 10;
    private const int ButtonHeightDip = 30;

    private RibbonLayout(SKRectI window, SKRectI strip, SKRectI grip, SKRectI dropZone, SKRectI snip, SKRectI open, bool isOnLeft, float scale)
    {
        Window = window;
        Strip = strip;
        Grip = grip;
        DropZone = dropZone;
        Snip = snip;
        Open = open;
        IsOnLeft = isOnLeft;
        Scale = scale;
    }

    /// <summary>In screen pixels. Everything else is relative to this.</summary>
    public SKRectI Window { get; }

    /// <summary>The area that reacts to the pointer while closed, a little wider than it looks.</summary>
    public SKRectI Strip { get; }

    /// <summary>The thin bar that can actually be seen while closed.</summary>
    public SKRectI Grip { get; }

    public SKRectI DropZone { get; }

    public SKRectI Snip { get; }

    public SKRectI Open { get; }

    public bool IsOnLeft { get; }

    public float Scale { get; }

    /// <param name="work">The monitor's work area, which already leaves out the taskbar.</param>
    /// <param name="position">How far down the edge the strip sits, from 0 to 1.</param>
    public static RibbonLayout Compute(SKRectI work, float scale, bool isOnLeft, double position)
    {
        int Px(int dip) => (int)Math.Round(dip * scale);

        var margin = Px(ScreenMarginDip);
        var width = Math.Min(Px(PanelWidthDip), work.Width);
        var height = Math.Min(Px(PanelHeightDip), Math.Max(1, work.Height - (2 * margin)));
        var stripHeight = Math.Min(Px(StripHeightDip), height);

        var share = double.IsFinite(position) ? Math.Clamp(position, 0, 1) : 0.5;
        var lowestCentre = work.Top + margin + (stripHeight / 2);
        var highestCentre = work.Bottom - margin - (stripHeight / 2);
        var centre = highestCentre < lowestCentre
            ? work.Top + (work.Height / 2)
            : lowestCentre + (int)Math.Round((highestCentre - lowestCentre) * share);

        var stripTop = centre - (stripHeight / 2);
        var windowTop = Math.Clamp(centre - (height / 2), work.Top + margin, Math.Max(work.Top + margin, work.Bottom - margin - height));

        // The strip has to stay inside the window, so the panel opens around the pointer.
        windowTop = Math.Clamp(windowTop, stripTop + stripHeight - height, stripTop);

        var windowLeft = isOnLeft ? work.Left : work.Right - width;
        var window = new SKRectI(windowLeft, windowTop, windowLeft + width, windowTop + height);

        var reach = Px(StripReachDip);
        var relativeTop = stripTop - windowTop;
        var strip = isOnLeft
            ? new SKRectI(0, relativeTop, reach, relativeTop + stripHeight)
            : new SKRectI(width - reach, relativeTop, width, relativeTop + stripHeight);

        var gripWidth = Px(GripWidthDip);
        var gripHeight = Px(GripHeightDip);
        var gripTop = relativeTop + ((stripHeight - gripHeight) / 2);
        var grip = isOnLeft
            ? new SKRectI(0, gripTop, gripWidth, gripTop + gripHeight)
            : new SKRectI(width - gripWidth, gripTop, width, gripTop + gripHeight);

        var inset = Px(InsetDip);
        var buttonHeight = Px(ButtonHeightDip);
        var buttonTop = height - inset - buttonHeight;
        var middle = width / 2;
        var dropZone = new SKRectI(inset, inset, width - inset, buttonTop - inset);
        var snip = new SKRectI(inset, buttonTop, middle - (inset / 4), buttonTop + buttonHeight);
        var open = new SKRectI(middle + (inset / 4), buttonTop, width - inset, buttonTop + buttonHeight);

        return new RibbonLayout(window, strip, grip, dropZone, snip, open, isOnLeft, scale);
    }

    public RibbonPart PartAt(SKPointI point) =>
        Snip.Contains(point) ? RibbonPart.Snip : Open.Contains(point) ? RibbonPart.Open : RibbonPart.None;
}

/// <summary>
/// A thin strip on the edge of the screen that opens into a small panel when pointed at, or as
/// soon as a drag reaches it. Closed, everything but the strip is fully transparent, which
/// Windows treats as not there at all, so clicks go straight through to whatever's beneath.
/// It steps aside for full-screen apps, and stays out of screenshots.
/// </summary>
internal sealed class EdgeRibbonWindow : AgentWindow
{
    private const uint HoverDelayMs = 180;
    private const uint WatchIntervalMs = 120;
    private const long CloseDelayMs = 500;
    private const int MoveThreshold = 4;
    private const int MenuOpen = 1;
    private const int MenuHide = 2;
    private const string Glyph = "";

    /// <summary>Barely there, but enough to count as part of the window for the pointer.</summary>
    private static readonly SKColor Touchable = new(0, 0, 0, 1);

    private static readonly IntPtr HoverTimer = 1;
    private static readonly IntPtr WatchTimer = 2;

    private readonly BackgroundSettingsStore _settings;
    private readonly Action _open;
    private readonly Action _snip;
    private readonly DropTarget _target;
    private readonly uint _appBarMessage;
    private bool _isAppBar;
    private RibbonLayout? _layout;
    private LayeredSurface? _surface;
    private SKRectI _work;
    private bool _isOnLeft;
    private double _position;
    private bool _isExpanded;
    private bool _isDragOver;
    private bool _isPressed;
    private bool _isMoving;
    private bool _isFullScreenApp;
    private long _lastNeeded;
    private RibbonPart _hot;
    private Native.POINT _pressPoint;

    public EdgeRibbonWindow(BackgroundSettingsStore settings, Action open, Action snip, Action<DroppedItems> dropped)
        : base(Native.WS_POPUP, Native.WS_EX_LAYERED | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST)
    {
        _settings = settings;
        _open = open;
        _snip = snip;
        _target = new DropTarget(SetDragOver, dropped);
        _appBarMessage = Native.RegisterWindowMessageW("PicKit.RibbonAppBar");

        var saved = settings.Load();
        _isOnLeft = saved.RibbonOnLeft;
        _position = saved.RibbonPosition;

        Native.SetWindowDisplayAffinity(Handle, Native.WDA_EXCLUDEFROMCAPTURE);
        var result = Native.RegisterDragDrop(Handle, _target);
        if (result != 0)
        {
            Log.Warn($"The ribbon could not accept drags: 0x{result:X8}");
        }

        RegisterAppBar();
        PlaceOnScreen();
        Native.ShowWindow(Handle, Native.SW_SHOWNOACTIVATE);
    }

    /// <summary>Settles on the main monitor, whose size can change at any time.</summary>
    public void PlaceOnScreen()
    {
        var monitor = Native.MonitorFromPoint(default, Native.MONITOR_DEFAULTTONEAREST);
        var work = InfoFor(monitor).rcWork;
        _work = new SKRectI(work.Left, work.Top, work.Right, work.Bottom);
        _layout = RibbonLayout.Compute(_work, ScaleFor(monitor), _isOnLeft, _position);

        if (_surface?.Width != _layout.Window.Width || _surface.Height != _layout.Window.Height)
        {
            _surface?.Dispose();
            _surface = new LayeredSurface(_layout.Window.Width, _layout.Window.Height);
        }

        Repaint();
    }

    public override void Dispose()
    {
        Native.RevokeDragDrop(Handle);
        if (_isAppBar)
        {
            var data = new Native.APPBARDATA { cbSize = Marshal.SizeOf<Native.APPBARDATA>(), hWnd = Handle };
            Native.SHAppBarMessage(Native.ABM_REMOVE, ref data);
        }

        _surface?.Dispose();
        base.Dispose();
    }

    /// <summary>Draws the ribbon. Separate from the window so it can be checked as a picture.</summary>
    internal static void Paint(SKCanvas canvas, RibbonLayout layout, bool isExpanded, bool isDragOver, RibbonPart hot)
    {
        if (!isExpanded)
        {
            PaintClosed(canvas, layout);
            return;
        }

        var scale = layout.Scale;
        var width = layout.Window.Width;
        var height = layout.Window.Height;
        var overhang = 12 * scale;

        // The side against the screen edge runs off it, so only the inner corners are rounded.
        var panel = layout.IsOnLeft
            ? new SKRect(-overhang, 0, width, height)
            : new SKRect(0, 0, width + overhang, height);

        using var fill = new SKPaint { Color = new SKColor(0x2B, 0x2B, 0x2B), IsAntialias = true };
        using var border = new SKPaint { Color = new SKColor(0x45, 0x45, 0x45), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = scale };
        canvas.DrawRoundRect(panel, 8 * scale, 8 * scale, fill);
        canvas.DrawRoundRect(panel, 8 * scale, 8 * scale, border);

        PaintDropZone(canvas, layout, isDragOver);
        PaintButton(canvas, layout.Snip, "Snip", hot == RibbonPart.Snip, scale);
        PaintButton(canvas, layout.Open, "Open PicKit", hot == RibbonPart.Open, scale);
    }

    protected override IntPtr OnMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == _appBarMessage)
        {
            OnAppBarNotice(wParam, lParam);
            return 0;
        }

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
            case Native.WM_TIMER:
                OnTimer(wParam);
                return 0;
            default:
                return base.OnMessage(message, wParam, lParam);
        }
    }

    private static void PaintClosed(SKCanvas canvas, RibbonLayout layout)
    {
        var strip = new SKRect(layout.Strip.Left, layout.Strip.Top, layout.Strip.Right, layout.Strip.Bottom);
        using var reach = new SKPaint { Color = Touchable };
        canvas.DrawRect(strip, reach);

        var radius = 2 * layout.Scale;
        var grip = new SKRect(layout.Grip.Left, layout.Grip.Top, layout.Grip.Right, layout.Grip.Bottom);
        var runsOff = layout.IsOnLeft
            ? new SKRect(grip.Left - radius, grip.Top, grip.Right, grip.Bottom)
            : new SKRect(grip.Left, grip.Top, grip.Right + radius, grip.Bottom);

        using var bar = new SKPaint { Color = new SKColor(0x8A, 0x8A, 0x8A, 0xB0), IsAntialias = true };
        canvas.DrawRoundRect(runsOff, radius, radius, bar);
    }

    private static void PaintDropZone(SKCanvas canvas, RibbonLayout layout, bool isDragOver)
    {
        var scale = layout.Scale;
        var zone = new SKRect(layout.DropZone.Left, layout.DropZone.Top, layout.DropZone.Right, layout.DropZone.Bottom);

        if (isDragOver)
        {
            using var glow = new SKPaint { Color = new SKColor(0x00, 0x78, 0xD4, 0x60), IsAntialias = true };
            canvas.DrawRoundRect(zone, 6 * scale, 6 * scale, glow);
        }

        using var dashes = SKPathEffect.CreateDash([5 * scale, 4 * scale], 0);
        using var edge = new SKPaint
        {
            Color = isDragOver ? new SKColor(0x4C, 0xC2, 0xFF) : new SKColor(0x70, 0x70, 0x70),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = scale,
            PathEffect = dashes,
        };

        canvas.DrawRoundRect(zone, 6 * scale, 6 * scale, edge);

        using var ink = new SKPaint { Color = new SKColor(0xD0, 0xD0, 0xD0), IsAntialias = true };
        using var typeface = SKTypeface.FromFamilyName("Segoe Fluent Icons") ?? SKTypeface.FromFamilyName("Segoe MDL2 Assets");
        using var iconFont = new SKFont(typeface, 18 * scale);
        using var textFont = new SKFont(TextFonts.Typeface(TextFonts.Default), 12 * scale);
        canvas.DrawText(Glyph, zone.MidX, zone.MidY - (2 * scale), SKTextAlign.Center, iconFont, ink);
        canvas.DrawText("Drop an image here", zone.MidX, zone.MidY + (16 * scale), SKTextAlign.Center, textFont, ink);
    }

    private static void PaintButton(SKCanvas canvas, SKRectI box, string text, bool isHot, float scale)
    {
        var rectangle = new SKRect(box.Left, box.Top, box.Right, box.Bottom);
        using var fill = new SKPaint { Color = isHot ? new SKColor(0x00, 0x78, 0xD4) : new SKColor(0x44, 0x44, 0x44), IsAntialias = true };
        using var font = new SKFont(TextFonts.Typeface(TextFonts.Default), 12 * scale);
        using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
        canvas.DrawRoundRect(rectangle, 5 * scale, 5 * scale, fill);
        canvas.DrawText(text, rectangle.MidX, rectangle.MidY + (font.Size * 0.35f), SKTextAlign.Center, font, ink);
    }

    private void RegisterAppBar()
    {
        var data = new Native.APPBARDATA
        {
            cbSize = Marshal.SizeOf<Native.APPBARDATA>(),
            hWnd = Handle,
            uCallbackMessage = _appBarMessage,
        };

        // Only so Windows reports full-screen apps. No screen space is reserved.
        _isAppBar = Native.SHAppBarMessage(Native.ABM_NEW, ref data) != UIntPtr.Zero;
    }

    private void OnAppBarNotice(IntPtr notice, IntPtr isOpening)
    {
        if (notice != Native.ABN_FULLSCREENAPP)
        {
            return;
        }

        _isFullScreenApp = isOpening != IntPtr.Zero && ForegroundFillsTheScreen();
        Native.ShowWindow(Handle, _isFullScreenApp ? Native.SW_HIDE : Native.SW_SHOWNOACTIVATE);
    }

    private bool ForegroundFillsTheScreen()
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == Handle || !Native.GetWindowRect(foreground, out var area))
        {
            return false;
        }

        // The desktop itself reports as full screen, and it's never in the way.
        var name = new StringBuilder(64);
        Native.GetClassNameW(foreground, name, name.Capacity);
        if (name.ToString() is "WorkerW" or "Progman" or "Shell_TrayWnd")
        {
            return false;
        }

        var monitor = InfoFor(Native.MonitorFromPoint(default, Native.MONITOR_DEFAULTTONEAREST)).rcMonitor;
        return area.Left <= monitor.Left && area.Top <= monitor.Top && area.Right >= monitor.Right && area.Bottom >= monitor.Bottom;
    }

    private void Repaint()
    {
        if (_surface is not { } surface || _layout is not { } layout)
        {
            return;
        }

        var (expanded, dragOver, hot) = (_isExpanded, _isDragOver, _hot);
        surface.Paint(canvas => Paint(canvas, layout, expanded, dragOver, hot));
        surface.Present(Handle, layout.Window.Left, layout.Window.Top);
    }

    private void SetDragOver(bool isDragOver)
    {
        _isDragOver = isDragOver;
        _lastNeeded = Environment.TickCount64;
        if (isDragOver && !_isExpanded)
        {
            Expand();
        }
        else
        {
            Repaint();
        }
    }

    private void Expand()
    {
        Native.KillTimer(Handle, HoverTimer);
        _isExpanded = true;
        _lastNeeded = Environment.TickCount64;
        Repaint();
        Native.SetTimer(Handle, WatchTimer, WatchIntervalMs, 0);
    }

    private void Collapse()
    {
        Native.KillTimer(Handle, WatchTimer);
        _isExpanded = false;
        _hot = RibbonPart.None;
        Repaint();
    }

    private SKPointI PointerInWindow()
    {
        Native.GetCursorPos(out var cursor);
        return _layout is { } layout ? new SKPointI(cursor.X - layout.Window.Left, cursor.Y - layout.Window.Top) : default;
    }

    private bool IsPointerInWindow()
    {
        Native.GetCursorPos(out var cursor);
        return _layout is { } layout && layout.Window.Contains(cursor.X, cursor.Y);
    }

    private void OnTimer(IntPtr timer)
    {
        if (timer == HoverTimer)
        {
            Native.KillTimer(Handle, HoverTimer);
            if (_layout is { } layout && !_isPressed && layout.Strip.Contains(PointerInWindow()))
            {
                Expand();
            }
        }
        else if (timer == WatchTimer)
        {
            WatchForLeaving();
        }
    }

    private void WatchForLeaving()
    {
        var now = Environment.TickCount64;
        if (_isDragOver || _isPressed || IsPointerInWindow())
        {
            _lastNeeded = now;
        }
        else if (now - _lastNeeded >= CloseDelayMs)
        {
            Collapse();
        }
    }

    private void OnPress()
    {
        Native.GetCursorPos(out _pressPoint);
        _isPressed = true;
        _isMoving = false;
        Native.SetCapture(Handle);
    }

    private void OnMove()
    {
        if (_layout is not { } layout)
        {
            return;
        }

        if (_isPressed)
        {
            if (!_isExpanded)
            {
                MoveAlongEdge();
            }

            return;
        }

        if (!_isExpanded)
        {
            Native.SetTimer(Handle, HoverTimer, HoverDelayMs, 0);
            return;
        }

        var hot = layout.PartAt(PointerInWindow());
        if (hot != _hot)
        {
            _hot = hot;
            Repaint();
        }
    }

    private void MoveAlongEdge()
    {
        Native.GetCursorPos(out var cursor);
        _isMoving |= Math.Abs(cursor.Y - _pressPoint.Y) > MoveThreshold || Math.Abs(cursor.X - _pressPoint.X) > MoveThreshold;
        if (_isMoving)
        {
            _isOnLeft = cursor.X < _work.Left + (_work.Width / 2);
            _position = (cursor.Y - _work.Top) / (double)Math.Max(1, _work.Height);
            PlaceOnScreen();
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

        if (_isMoving)
        {
            _isMoving = false;
            var (isOnLeft, position) = (_isOnLeft, _position);
            _settings.Update(settings =>
            {
                settings.RibbonOnLeft = isOnLeft;
                settings.RibbonPosition = position;
            });
        }
        else if (!_isExpanded)
        {
            Expand();
        }
        else if (_layout is { } layout)
        {
            Act(layout.PartAt(PointerInWindow()));
        }
    }

    private void Act(RibbonPart part)
    {
        switch (part)
        {
            case RibbonPart.Snip:
                Collapse();
                _snip();
                break;
            case RibbonPart.Open:
                Collapse();
                _open();
                break;
        }
    }

    private void ShowMenu()
    {
        var menu = Native.CreatePopupMenu();
        Native.AppendMenuW(menu, Native.MF_STRING, MenuOpen, "Open PicKit");
        Native.AppendMenuW(menu, Native.MF_STRING, MenuHide, "Hide this ribbon");

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
            _settings.Update(settings => settings.ShowRibbon = false);
            Native.ShowWindow(Handle, Native.SW_HIDE);
        }
    }
}
