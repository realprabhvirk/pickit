using System.Runtime.InteropServices;
using PicKit.Core.Imaging;
using SkiaSharp;

namespace PicKit.Background;

/// <summary>A picture of every monitor, taken in one instant, ready to snip from.</summary>
/// <param name="Image">The owner disposes this.</param>
/// <param name="Origin">Where the picture's top-left corner sits on the screen.</param>
/// <param name="Monitor">The monitor under the mouse, in the picture's own pixels.</param>
/// <param name="Scale">That monitor's display scale.</param>
internal sealed record FrozenScreen(ImageBuffer Image, SKPointI Origin, SKRectI Monitor, float Scale);

/// <summary>
/// Copies the screen with a plain GDI read. That shows no border or flash, plays no sound, and
/// leaves the clipboard alone. Windows that ask not to be captured, such as protected video,
/// come out black, and that's left as it is.
/// </summary>
internal static class ScreenCapture
{
    public static FrozenScreen Freeze()
    {
        Native.GetCursorPos(out var cursor);
        var handle = Native.MonitorFromPoint(cursor, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfoW(handle, ref info))
        {
            throw new System.ComponentModel.Win32Exception();
        }

        var left = Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN);
        var top = Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN);
        var width = Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN);
        var height = Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN);

        var image = Capture(left, top, width, height);
        var monitor = new SKRectI(
            info.rcMonitor.Left - left,
            info.rcMonitor.Top - top,
            info.rcMonitor.Right - left,
            info.rcMonitor.Bottom - top);

        var scale = Native.GetDpiForMonitor(handle, Native.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi / 96f : 1;
        return new FrozenScreen(image, new SKPointI(left, top), monitor, scale);
    }

    private static unsafe ImageBuffer Capture(int left, int top, int width, int height)
    {
        var header = new Native.BITMAPINFOHEADER
        {
            biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
        };

        var screen = Native.GetDC(0);
        var memory = Native.CreateCompatibleDC(screen);
        var bitmap = Native.CreateDIBSection(screen, ref header, 0, out var bits, 0, 0);
        var previous = Native.SelectObject(memory, bitmap);
        try
        {
            if (!Native.BitBlt(memory, 0, 0, width, height, screen, left, top, Native.SRCCOPY))
            {
                throw new System.ComponentModel.Win32Exception();
            }

            var image = ImageBuffer.Create(width, height);
            var pixels = image.Pixels;
            new Span<uint>((void*)bits, width * height).CopyTo(pixels);

            // The screen has no alpha of its own, and GDI leaves that byte empty.
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] |= 0xFF000000;
            }

            return image;
        }
        finally
        {
            Native.SelectObject(memory, previous);
            Native.DeleteObject(bitmap);
            Native.DeleteDC(memory);
            Native.ReleaseDC(0, screen);
        }
    }
}
