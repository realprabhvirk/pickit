using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using PicKit.Core.Diagnostics;
using PicKit.Core.Imaging;

namespace PicKit.Services;

/// <summary>The Windows clipboard. Replaced in tests, so they never disturb the real one.</summary>
public class ClipboardService
{
    private const uint CfDib = 8;
    private const uint CfDibV5 = 17;
    private const uint CfHDrop = 15;
    private const uint MoveableMemory = 0x0002;

    private const int OpenAttempts = 10;
    private static readonly TimeSpan OpenRetryDelay = TimeSpan.FromMilliseconds(30);

    /// <summary>Replaces the clipboard's contents. Returns false if another app held it too long.</summary>
    public virtual bool Set(IReadOnlyList<ClipboardItem> items)
    {
        if (!TryOpen())
        {
            return false;
        }

        try
        {
            if (!EmptyClipboard())
            {
                return false;
            }

            foreach (var item in items)
            {
                if (!Put(FormatId(item.Format), item.Data))
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    public virtual bool ContainsImage() =>
        IsClipboardFormatAvailable(RegisterClipboardFormat(ClipboardCodec.PngFormat))
        || IsClipboardFormatAvailable(CfDib)
        || IsClipboardFormatAvailable(CfDibV5)
        || IsClipboardFormatAvailable(CfHDrop);

    /// <summary>The image on the clipboard, or null. The caller owns the result.</summary>
    public virtual ImageBuffer? ReadImage()
    {
        if (!TryOpen())
        {
            return null;
        }

        try
        {
            return ClipboardCodec.Decode(format => Read(FormatId(format)));
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Paths of image files copied in File Explorer.</summary>
    public virtual IReadOnlyList<string> ReadFiles()
    {
        if (!TryOpen())
        {
            return [];
        }

        try
        {
            var handle = GetClipboardData(CfHDrop);
            if (handle == IntPtr.Zero)
            {
                return [];
            }

            var count = DragQueryFile(handle, uint.MaxValue, null, 0);
            var paths = new List<string>((int)count);
            for (uint i = 0; i < count; i++)
            {
                var length = DragQueryFile(handle, i, null, 0);
                var buffer = new StringBuilder((int)length + 1);
                DragQueryFile(handle, i, buffer, length + 1);
                paths.Add(buffer.ToString());
            }

            return paths.Where(ImageFormats.IsOpenable).ToList();
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static uint FormatId(string format) => format switch
    {
        ClipboardCodec.DibFormat => CfDib,
        ClipboardCodec.DibV5Format => CfDibV5,
        _ => RegisterClipboardFormat(format),
    };

    private static bool TryOpen()
    {
        // The clipboard is one shared lock, and clipboard managers often hold it for a moment.
        var owner = Application.Current?.MainWindow is { } window ? new WindowInteropHelper(window).Handle : IntPtr.Zero;
        for (var attempt = 0; attempt < OpenAttempts; attempt++)
        {
            if (OpenClipboard(owner))
            {
                return true;
            }

            Thread.Sleep(OpenRetryDelay);
        }

        Log.Warn("The clipboard stayed locked by another app");
        return false;
    }

    private static bool Put(uint format, byte[] data)
    {
        var memory = GlobalAlloc(MoveableMemory, (nuint)data.Length);
        if (memory == IntPtr.Zero)
        {
            return false;
        }

        var target = GlobalLock(memory);
        if (target == IntPtr.Zero)
        {
            GlobalFree(memory);
            return false;
        }

        Marshal.Copy(data, 0, target, data.Length);
        GlobalUnlock(memory);

        // Once this succeeds, Windows owns the memory.
        if (SetClipboardData(format, memory) == IntPtr.Zero)
        {
            GlobalFree(memory);
            return false;
        }

        return true;
    }

    private static byte[]? Read(uint format)
    {
        var handle = GetClipboardData(format);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var size = (long)(ulong)GlobalSize(handle);
        var source = GlobalLock(handle);
        if (source == IntPtr.Zero || size is <= 0 or > int.MaxValue)
        {
            return null;
        }

        try
        {
            var data = new byte[size];
            Marshal.Copy(source, data, 0, (int)size);
            return data;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClipboardFormatW")]
    private static extern uint RegisterClipboardFormat(string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr memory);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr memory);

    [DllImport("kernel32.dll")]
    private static extern nuint GlobalSize(IntPtr memory);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "DragQueryFileW")]
    private static extern uint DragQueryFile(IntPtr drop, uint index, StringBuilder? file, uint size);
}
