using System.Runtime.InteropServices;
using PicKit.Core.Imaging;

namespace PicKit.Background;

/// <summary>
/// Copies the screen with a plain GDI read. That shows no border or flash, plays no sound, and
/// leaves the clipboard alone. Windows that ask not to be captured, such as protected video,
/// come out black, and that's left as it is.
/// </summary>
internal static class ScreenCapture
{
    public static ImageBuffer MonitorUnderCursor()
    {
        Native.GetCursorPos(out var cursor);
        var monitor = Native.MonitorFromPoint(cursor, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfoW(monitor, ref info))
        {
            throw new System.ComponentModel.Win32Exception();
        }

        return Capture(info.rcMonitor);
    }

    private static unsafe ImageBuffer Capture(Native.RECT area)
    {
        var width = area.Width;
        var height = area.Height;
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
            if (!Native.BitBlt(memory, 0, 0, width, height, screen, area.Left, area.Top, Native.SRCCOPY))
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
