using System.Globalization;

namespace PicKit.Core.Files;

public static class FileSizeText
{
    private const double Kilobyte = 1024;
    private const double Megabyte = Kilobyte * 1024;
    private const double Gigabyte = Megabyte * 1024;

    public static string Format(long bytes)
    {
        var culture = CultureInfo.CurrentCulture;
        return bytes switch
        {
            >= (long)Gigabyte => string.Format(culture, "{0:0.##} GB", bytes / Gigabyte),
            >= (long)Megabyte => string.Format(culture, "{0:0.##} MB", bytes / Megabyte),
            >= (long)Kilobyte => string.Format(culture, "{0:0} KB", bytes / Kilobyte),
            _ => string.Format(culture, "{0} bytes", bytes),
        };
    }
}
