using System.Runtime.InteropServices;
using PicKit.Core.Diagnostics;
using PicKit.Core.Imaging;

namespace PicKit.Background;

/// <summary>Puts an image on the clipboard from the helper, which has no WPF to do it with.</summary>
internal static class ClipboardWriter
{
    private const uint Dib = 8;
    private const uint DibV5 = 17;
    private const uint MoveableMemory = 0x0002;
    private const int OpenAttempts = 10;
    private const int OpenRetryMilliseconds = 30;

    /// <summary>Returns false if another app held the clipboard for too long.</summary>
    public static bool Set(IntPtr owner, IReadOnlyList<ClipboardItem> items)
    {
        if (!TryOpen(owner))
        {
            Log.Warn("The clipboard was busy, so a screenshot wasn't put on it");
            return false;
        }

        try
        {
            return EmptyClipboard() && items.All(item => Put(FormatId(item.Format), item.Data));
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool TryOpen(IntPtr owner)
    {
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (OpenClipboard(owner))
            {
                return true;
            }

            Thread.Sleep(OpenRetryMilliseconds);
        }

        return false;
    }

    private static uint FormatId(string format) => format switch
    {
        ClipboardCodec.DibFormat => Dib,
        ClipboardCodec.DibV5Format => DibV5,
        _ => RegisterClipboardFormatW(format),
    };

    private static bool Put(uint format, byte[] data)
    {
        var memory = GlobalAlloc(MoveableMemory, (nuint)data.Length);
        if (memory == IntPtr.Zero)
        {
            return false;
        }

        var pointer = GlobalLock(memory);
        if (pointer == IntPtr.Zero)
        {
            GlobalFree(memory);
            return false;
        }

        Marshal.Copy(data, 0, pointer, data.Length);
        GlobalUnlock(memory);

        // Once this succeeds, the memory belongs to the clipboard.
        if (SetClipboardData(format, memory) == IntPtr.Zero)
        {
            GlobalFree(memory);
            return false;
        }

        return true;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterClipboardFormatW(string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
