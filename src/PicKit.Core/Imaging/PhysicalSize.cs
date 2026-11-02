using System.Globalization;

namespace PicKit.Core.Imaging;

public enum SizeUnit
{
    Pixels,
    Millimetres,
    Centimetres,
}

/// <summary>Converts between pixels and print sizes, which only relate through a resolution.</summary>
public static class PhysicalSize
{
    public const int DefaultDpi = 300;
    public const int MaxDpi = 9600;

    private const double MillimetresPerInch = 25.4;

    public static bool IsValidDpi(int dpi) => dpi is >= 1 and <= MaxDpi;

    public static double FromPixels(int pixels, SizeUnit unit, int dpi) => unit switch
    {
        SizeUnit.Millimetres => pixels * MillimetresPerInch / dpi,
        SizeUnit.Centimetres => pixels * MillimetresPerInch / dpi / 10,
        _ => pixels,
    };

    public static int ToPixels(double value, SizeUnit unit, int dpi)
    {
        var pixels = unit switch
        {
            SizeUnit.Millimetres => value * dpi / MillimetresPerInch,
            SizeUnit.Centimetres => value * 10 * dpi / MillimetresPerInch,
            _ => value,
        };

        return (int)Math.Clamp(Math.Round(pixels), 0, int.MaxValue);
    }

    /// <summary>Enough decimals that a size read back lands on the same pixel at normal resolutions.</summary>
    public static string Format(double value, SizeUnit unit) => unit switch
    {
        SizeUnit.Millimetres => value.ToString("0.##", CultureInfo.CurrentCulture),
        SizeUnit.Centimetres => value.ToString("0.###", CultureInfo.CurrentCulture),
        _ => value.ToString("0", CultureInfo.CurrentCulture),
    };

    /// <summary>Accepts the PC's own decimal mark, and a dot wherever that isn't one.</summary>
    public static bool TryParse(string text, out double value) =>
        (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        && double.IsFinite(value);
}
