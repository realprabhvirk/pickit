using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using PicKit.Core.Diagnostics;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace PicKit.Core.Background;

/// <summary>An image whose bytes arrived with the drag, with whatever name its source gave it.</summary>
public sealed record DroppedFile(string Name, byte[] Data);

/// <summary>What was dragged in. At most one kind is ever filled in.</summary>
public sealed record DroppedItems(IReadOnlyList<string> Paths, DroppedFile? File, Uri? Url)
{
    public static DroppedItems Nothing { get; } = new([], null, null);
}

/// <summary>
/// Reads a drag from File Explorer or a web page. Browsers offer an image several ways at once,
/// so this takes the best available: real files, then the image's own bytes, then its address.
/// Everything here comes from another app, so all of it is treated as untrusted.
/// </summary>
public static partial class DropReader
{
    private const short FileListFormat = 15;
    private const int MaxBytes = 256 * 1024 * 1024;
    private const int DescriptorNameOffset = 4 + 72;
    private const int DescriptorNameBytes = 520;

    private static readonly short FileDescriptorFormat = Register("FileGroupDescriptorW");
    private static readonly short FileContentsFormat = Register("FileContents");
    private static readonly short PngFormat = Register("PNG");
    private static readonly short UrlFormat = Register("UniformResourceLocatorW");
    private static readonly short HtmlFormat = Register("HTML Format");

    public static DroppedItems Read(ComDataObject data)
    {
        try
        {
            var paths = ReadPaths(data);
            if (paths.Count > 0)
            {
                return new DroppedItems(paths, null, null);
            }

            if ((ReadVirtualFile(data) ?? ReadPng(data)) is { } file)
            {
                return new DroppedItems([], file, null);
            }

            return FromAddress(ReadImageAddress(data) ?? ReadUrl(data));
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException or IndexOutOfRangeException)
        {
            Log.Warn("Could not read what was dropped", ex);
            return DroppedItems.Nothing;
        }
    }

    /// <summary>Turns an address into a download, or into bytes if it's a data: address.</summary>
    internal static DroppedItems FromAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri))
        {
            return DroppedItems.Nothing;
        }

        if (uri.Scheme == "data")
        {
            return FromDataAddress(address.Trim());
        }

        return uri.Scheme is "http" or "https" ? new DroppedItems([], null, uri) : DroppedItems.Nothing;
    }

    internal static IReadOnlyList<string> ParseFileList(byte[] block)
    {
        const int isWideOffset = 16;
        const int minimumHeader = 20;
        if (block.Length < minimumHeader || BitConverter.ToInt32(block, isWideOffset) == 0)
        {
            return [];
        }

        var start = BitConverter.ToInt32(block, 0);
        if (start < minimumHeader || start >= block.Length)
        {
            return [];
        }

        return Encoding.Unicode.GetString(block, start, (block.Length - start) & ~1)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    internal static string? ParseDescriptorName(byte[] block)
    {
        if (block.Length < DescriptorNameOffset + DescriptorNameBytes || BitConverter.ToInt32(block, 0) < 1)
        {
            return null;
        }

        var name = Encoding.Unicode.GetString(block, DescriptorNameOffset, DescriptorNameBytes);
        var end = name.IndexOf('\0');
        return end >= 0 ? name[..end] : name;
    }

    internal static string? ParseImageAddress(string html)
    {
        try
        {
            var match = ImageSource().Match(html);
            return match.Success ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    [GeneratedRegex("""<img\b[^>]*?\bsrc\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 500)]
    private static partial Regex ImageSource();

    private static DroppedItems FromDataAddress(string address)
    {
        const string marker = ";base64,";
        var start = address.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return DroppedItems.Nothing;
        }

        try
        {
            return new DroppedItems([], new DroppedFile("Dropped image", Convert.FromBase64String(address[(start + marker.Length)..])), null);
        }
        catch (FormatException)
        {
            return DroppedItems.Nothing;
        }
    }

    private static IReadOnlyList<string> ReadPaths(ComDataObject data) =>
        ReadBytes(data, FileListFormat) is { } block ? ParseFileList(block) : [];

    private static DroppedFile? ReadVirtualFile(ComDataObject data)
    {
        if (ReadBytes(data, FileDescriptorFormat) is not { } descriptor || ParseDescriptorName(descriptor) is not { } name)
        {
            return null;
        }

        return ReadBytes(data, FileContentsFormat, index: 0) is { Length: > 0 } contents ? new DroppedFile(name, contents) : null;
    }

    private static DroppedFile? ReadPng(ComDataObject data) =>
        ReadBytes(data, PngFormat) is { Length: > 0 } png ? new DroppedFile("Dropped image", png) : null;

    private static string? ReadImageAddress(ComDataObject data) =>
        ReadBytes(data, HtmlFormat) is { } html ? ParseImageAddress(Encoding.UTF8.GetString(html)) : null;

    private static string? ReadUrl(ComDataObject data) =>
        ReadBytes(data, UrlFormat) is { } url ? Encoding.Unicode.GetString(url).Split('\0')[0] : null;

    private static byte[]? ReadBytes(ComDataObject data, short format, int index = -1)
    {
        var request = new FORMATETC
        {
            cfFormat = format,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = index,
            tymed = TYMED.TYMED_HGLOBAL | TYMED.TYMED_ISTREAM,
        };

        if (data.QueryGetData(ref request) != 0)
        {
            return null;
        }

        data.GetData(ref request, out var medium);
        try
        {
            return medium.tymed switch
            {
                TYMED.TYMED_HGLOBAL => ReadGlobal(medium.unionmember),
                TYMED.TYMED_ISTREAM => ReadStream(medium.unionmember),
                _ => null,
            };
        }
        finally
        {
            ReleaseStgMedium(ref medium);
        }
    }

    private static byte[]? ReadGlobal(IntPtr handle)
    {
        var size = (long)GlobalSize(handle);
        if (size is <= 0 or > MaxBytes)
        {
            return null;
        }

        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var bytes = new byte[size];
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static byte[]? ReadStream(IntPtr handle)
    {
        var stream = (IStream)Marshal.GetObjectForIUnknown(handle);
        using var output = new MemoryStream();
        var buffer = new byte[256 * 1024];
        var read = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            while (true)
            {
                stream.Read(buffer, buffer.Length, read);
                var count = Marshal.ReadInt32(read);
                if (count == 0)
                {
                    return output.ToArray();
                }

                output.Write(buffer, 0, count);
                if (output.Length > MaxBytes)
                {
                    return null;
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(read);
        }
    }

    private static short Register(string name) => (short)RegisterClipboardFormatW(name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClipboardFormatW(string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr GlobalSize(IntPtr handle);

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
}
