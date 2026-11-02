using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PicKit.Core.Diagnostics;
using PicKit.Core.Imaging;

namespace PicKit.Services;

/// <summary>
/// Falls back to Windows Imaging Component for formats Skia doesn't read, which covers TIFF
/// and any codec the user has installed from the Store, such as HEIF.
/// </summary>
internal static class WicDecoder
{
    public static DecodedImage? TryDecode(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            ImageLimits.EnsureFits(frame.PixelWidth, frame.PixelHeight);

            var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
            var pixels = ImageBuffer.Create(frame.PixelWidth, frame.PixelHeight);
            try
            {
                converted.CopyPixels(
                    System.Windows.Int32Rect.Empty,
                    pixels.Bitmap.GetPixels(),
                    (int)pixels.ByteCount,
                    pixels.Bitmap.RowBytes);

                return new DecodedImage(pixels, FormatOf(decoder)) { FrameCount = decoder.Frames.Count };
            }
            catch
            {
                pixels.Dispose();
                throw;
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or InvalidOperationException
            or ArgumentException or IOException or OverflowException or System.Runtime.InteropServices.COMException)
        {
            Log.Info($"Windows imaging could not decode the image: {ex.Message}");
            return null;
        }
    }

    private static ImageFormat FormatOf(BitmapDecoder decoder)
    {
        var extensions = decoder.CodecInfo?.FileExtensions ?? string.Empty;
        foreach (var extension in extensions.Split(','))
        {
            var format = ImageFormats.FromExtension(extension.Trim());
            if (format != ImageFormat.Unknown)
            {
                return format;
            }
        }

        return ImageFormat.Unknown;
    }
}
