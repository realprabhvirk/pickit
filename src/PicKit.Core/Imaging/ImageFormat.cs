namespace PicKit.Core.Imaging;

public enum ImageFormat
{
    Unknown,
    Jpeg,
    Png,
    WebP,
    Gif,
    Bmp,
    Tiff,
    Heif,
    Avif,
    Ico,
}

public static class ImageFormats
{
    private static readonly Dictionary<string, ImageFormat> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = ImageFormat.Jpeg,
        [".jpeg"] = ImageFormat.Jpeg,
        [".jpe"] = ImageFormat.Jpeg,
        [".jfif"] = ImageFormat.Jpeg,
        [".png"] = ImageFormat.Png,
        [".webp"] = ImageFormat.WebP,
        [".gif"] = ImageFormat.Gif,
        [".bmp"] = ImageFormat.Bmp,
        [".dib"] = ImageFormat.Bmp,
        [".tif"] = ImageFormat.Tiff,
        [".tiff"] = ImageFormat.Tiff,
        [".heic"] = ImageFormat.Heif,
        [".heif"] = ImageFormat.Heif,
        [".avif"] = ImageFormat.Avif,
        [".ico"] = ImageFormat.Ico,
    };

    /// <summary>File extensions PicKit will try to open, used for dialogs and folder scans.</summary>
    public static IReadOnlyCollection<string> OpenableExtensions => ByExtension.Keys;

    public static ImageFormat FromExtension(string? pathOrExtension)
    {
        if (string.IsNullOrEmpty(pathOrExtension))
        {
            return ImageFormat.Unknown;
        }

        var extension = Path.GetExtension(pathOrExtension);
        return ByExtension.GetValueOrDefault(extension, ImageFormat.Unknown);
    }

    public static bool IsOpenable(string path) => FromExtension(path) != ImageFormat.Unknown;

    /// <summary>PicKit can only write the formats it has a reliable encoder for.</summary>
    public static bool CanEncode(ImageFormat format) =>
        format is ImageFormat.Jpeg or ImageFormat.Png or ImageFormat.WebP;

    public static bool SupportsTransparency(ImageFormat format) =>
        format is ImageFormat.Png or ImageFormat.WebP;

    public static string Extension(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => ".jpg",
        ImageFormat.Png => ".png",
        ImageFormat.WebP => ".webp",
        ImageFormat.Gif => ".gif",
        ImageFormat.Bmp => ".bmp",
        ImageFormat.Tiff => ".tiff",
        ImageFormat.Heif => ".heic",
        ImageFormat.Avif => ".avif",
        ImageFormat.Ico => ".ico",
        _ => string.Empty,
    };

    public static string DisplayName(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => "JPEG",
        ImageFormat.Png => "PNG",
        ImageFormat.WebP => "WebP",
        ImageFormat.Gif => "GIF",
        ImageFormat.Bmp => "BMP",
        ImageFormat.Tiff => "TIFF",
        ImageFormat.Heif => "HEIC",
        ImageFormat.Avif => "AVIF",
        ImageFormat.Ico => "ICO",
        _ => "Image",
    };
}
