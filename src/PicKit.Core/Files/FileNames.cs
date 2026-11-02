using System.Text;

namespace PicKit.Core.Files;

/// <summary>Naming rules for everything PicKit writes. File names are treated as untrusted input.</summary>
public static class FileNames
{
    private const int MaxNameLength = 240;

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Makes a string safe to use as a single file name, keeping as much of it as possible.</summary>
    public static string Sanitize(string name, string fallback = "image")
    {
        var builder = new StringBuilder(name.Length);
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var character in name)
        {
            builder.Append(Array.IndexOf(invalid, character) >= 0 || char.IsControl(character) ? '_' : character);
        }

        // Windows silently drops trailing dots and spaces, which would change the name.
        var clean = builder.ToString().Trim().TrimEnd('.', ' ');
        if (clean.Length == 0)
        {
            return fallback;
        }

        if (IsReservedDeviceName(clean))
        {
            clean = "_" + clean;
        }

        return clean.Length > MaxNameLength ? clean[..MaxNameLength].TrimEnd('.', ' ') : clean;
    }

    public static bool IsReservedDeviceName(string name)
    {
        var stem = name;
        var dot = name.IndexOf('.');
        if (dot >= 0)
        {
            stem = name[..dot];
        }

        return ReservedDeviceNames.Contains(stem);
    }

    /// <summary>The path "photo.jpg" becomes "photo-edited.jpg" with suffix "edited".</summary>
    public static string WithSuffix(string path, string suffix, string? newExtension = null)
    {
        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = newExtension ?? Path.GetExtension(path);
        return Path.Combine(folder, $"{stem}-{suffix}{extension}");
    }

    /// <summary>Returns the path unchanged if free, otherwise the first "name (2).ext" that is.</summary>
    public static string MakeUnique(string path, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;
        if (!exists(path))
        {
            return path;
        }

        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var attempt = 2; attempt < 100_000; attempt++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({attempt}){extension}");
            if (!exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Couldn't find a free file name for {path}.");
    }
}
