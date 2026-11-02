using System.Runtime.InteropServices;
using PicKit.Core.Diagnostics;
using SkiaSharp;

namespace PicKit.Background;

/// <summary>A plain Win32 window. Its subclasses handle only the messages they care about.</summary>
internal abstract class AgentWindow : IDisposable
{
    private const string ClassName = "PicKit.Background";
    private const string Title = "PicKit helper";

    private static readonly Native.WindowProcedure Procedure = Route;
    private static readonly Dictionary<IntPtr, AgentWindow> Windows = [];
    private static bool _isClassRegistered;

    protected AgentWindow(int style, int extendedStyle)
    {
        RegisterClass();
        var instance = Native.GetModuleHandleW(null);
        Handle = Native.CreateWindowExW(extendedStyle, ClassName, Title, style, 0, 0, 0, 0, 0, 0, instance, 0);
        if (Handle == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception();
        }

        Windows[Handle] = this;
    }

    public IntPtr Handle { get; }

    public virtual void Dispose()
    {
        Windows.Remove(Handle);
        Native.DestroyWindow(Handle);
    }

    /// <summary>
    /// A close request to any of these windows, such as from Windows updating its files, ends
    /// the whole helper. Left to the default, it would destroy just that one window.
    /// </summary>
    protected virtual IntPtr OnMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == Native.WM_CLOSE)
        {
            Native.PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return Native.DefWindowProcW(Handle, message, wParam, lParam);
    }

    /// <summary>Pixels per layout unit on a monitor, so these windows match the rest of Windows.</summary>
    protected static float ScaleFor(IntPtr monitor) =>
        Native.GetDpiForMonitor(monitor, Native.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi / 96f : 1;

    protected static Native.MONITORINFO InfoFor(IntPtr monitor)
    {
        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfoW(monitor, ref info);
        return info;
    }

    protected static IntPtr MonitorUnderCursor()
    {
        Native.GetCursorPos(out var cursor);
        return Native.MonitorFromPoint(cursor, Native.MONITOR_DEFAULTTONEAREST);
    }

    private static void RegisterClass()
    {
        if (_isClassRegistered)
        {
            return;
        }

        var windowClass = new Native.WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<Native.WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Procedure),
            hInstance = Native.GetModuleHandleW(null),
            hCursor = Native.LoadCursorW(0, Native.IDC_ARROW),
            lpszClassName = ClassName,
        };

        if (Native.RegisterClassExW(ref windowClass) == 0)
        {
            throw new System.ComponentModel.Win32Exception();
        }

        _isClassRegistered = true;
    }

    private static IntPtr Route(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            return Windows.TryGetValue(window, out var target)
                ? target.OnMessage(message, wParam, lParam)
                : Native.DefWindowProcW(window, message, wParam, lParam);
        }
        catch (Exception ex)
        {
            // Nothing can catch this above here, and it would end the helper for good.
            Log.Error("The background helper hit a problem", ex);
            return IntPtr.Zero;
        }
    }
}

/// <summary>Off-screen pixels for a window that can have soft, see-through edges.</summary>
internal sealed class LayeredSurface : IDisposable
{
    private readonly IntPtr _dc;
    private readonly IntPtr _bitmap;
    private readonly IntPtr _previous;
    private readonly IntPtr _bits;

    public LayeredSurface(int width, int height)
    {
        Width = width;
        Height = height;

        var header = new Native.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
        };

        var screen = Native.GetDC(0);
        _dc = Native.CreateCompatibleDC(screen);
        _bitmap = Native.CreateDIBSection(screen, ref header, 0, out _bits, 0, 0);
        _previous = Native.SelectObject(_dc, _bitmap);
        Native.ReleaseDC(0, screen);
    }

    public int Width { get; }

    public int Height { get; }

    public void Paint(Action<SKCanvas> paint)
    {
        var info = new SKImageInfo(Width, Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info, _bits, Width * 4);
        surface.Canvas.Clear(SKColors.Transparent);
        paint(surface.Canvas);
    }

    public void Present(IntPtr window, int x, int y, byte opacity = 255)
    {
        const byte perPixelAlpha = 1;
        var position = new Native.POINT { X = x, Y = y };
        var size = new Native.SIZE { Width = Width, Height = Height };
        var origin = default(Native.POINT);
        var blend = new Native.BLENDFUNCTION { SourceConstantAlpha = opacity, AlphaFormat = perPixelAlpha };
        Native.UpdateLayeredWindow(window, 0, ref position, ref size, _dc, ref origin, 0, ref blend, Native.ULW_ALPHA);
    }

    public void Dispose()
    {
        Native.SelectObject(_dc, _previous);
        Native.DeleteObject(_bitmap);
        Native.DeleteDC(_dc);
    }
}
