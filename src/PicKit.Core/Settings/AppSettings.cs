using System.Text.Json.Serialization;

namespace PicKit.Core.Settings;

public enum MetadataHandling
{
    /// <summary>Exports carry no EXIF, GPS or similar data.</summary>
    Remove,

    /// <summary>JPEG exports keep the original's photo details, except after privacy edits.</summary>
    Keep,
}

public enum AppAppearance
{
    System,
    Light,
    Dark,
}

public sealed class WindowPlacementSettings
{
    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public bool IsMaximized { get; set; }
}

public sealed class AppSettings
{
    public bool ConfirmOverwrite { get; set; } = true;

    public bool RememberWindow { get; set; } = true;

    public AppAppearance Appearance { get; set; } = AppAppearance.System;

    public int JpegQuality { get; set; } = 90;

    public int WebPQuality { get; set; } = 82;

    /// <summary>Format suggested for images with no file of their own, such as clipboard images.</summary>
    public string DefaultExportFormat { get; set; } = "Png";

    /// <summary>Colour placed behind transparent pixels when the target format has no alpha.</summary>
    public string TransparencyBackground { get; set; } = "#FFFFFF";

    public MetadataHandling MetadataHandling { get; set; } = MetadataHandling.Remove;

    public WindowPlacementSettings? Window { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<string>))]
public sealed partial class SettingsJsonContext : JsonSerializerContext;
