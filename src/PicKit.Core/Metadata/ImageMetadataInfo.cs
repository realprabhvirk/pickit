using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.Xmp;
using PicKit.Core.Diagnostics;

namespace PicKit.Core.Metadata;

public sealed record MetadataField(string Group, string Name, string Value);

/// <summary>What a file reveals about the photo and the person who took it.</summary>
public sealed record ImageMetadataInfo(IReadOnlyList<MetadataField> Fields, bool HasPrivateData)
{
    public static ImageMetadataInfo Empty { get; } = new([], false);

    public bool HasLocation => Fields.Any(item => item.Group == "Location");

    public static ImageMetadataInfo Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Read(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not read metadata", ex);
            return Empty;
        }
    }

    public static ImageMetadataInfo Read(byte[] file)
    {
        using var stream = new MemoryStream(file, writable: false);
        return Read(stream);
    }

    private static ImageMetadataInfo Read(Stream stream)
    {
        IReadOnlyList<MetadataExtractor.Directory> directories;
        try
        {
            directories = ImageMetadataReader.ReadMetadata(stream);
        }
        catch (Exception ex) when (ex is ImageProcessingException or IOException or ArgumentException or IndexOutOfRangeException)
        {
            // Metadata is optional. A file with broken metadata can still be a fine image.
            Log.Info($"No readable metadata: {ex.Message}");
            return Empty;
        }

        var fields = new List<MetadataField>();
        Add(fields, "Camera", "Make", directories.OfType<ExifIfd0Directory>(), ExifDirectoryBase.TagMake);
        Add(fields, "Camera", "Model", directories.OfType<ExifIfd0Directory>(), ExifDirectoryBase.TagModel);
        Add(fields, "Camera", "Lens", directories.OfType<ExifSubIfdDirectory>(), ExifDirectoryBase.TagLensModel);
        Add(fields, "Photo", "Taken", directories.OfType<ExifSubIfdDirectory>(), ExifDirectoryBase.TagDateTimeOriginal);
        Add(fields, "Photo", "Exposure", directories.OfType<ExifSubIfdDirectory>(), ExifDirectoryBase.TagExposureTime);
        Add(fields, "Photo", "Aperture", directories.OfType<ExifSubIfdDirectory>(), ExifDirectoryBase.TagFNumber);
        Add(fields, "Photo", "ISO", directories.OfType<ExifSubIfdDirectory>(), ExifDirectoryBase.TagIsoEquivalent);
        Add(fields, "Author", "Artist", directories.OfType<ExifIfd0Directory>(), ExifDirectoryBase.TagArtist);
        Add(fields, "Author", "Copyright", directories.OfType<ExifIfd0Directory>(), ExifDirectoryBase.TagCopyright);
        Add(fields, "Software", "Software", directories.OfType<ExifIfd0Directory>(), ExifDirectoryBase.TagSoftware);

        foreach (var gps in directories.OfType<GpsDirectory>())
        {
            if (gps.GetGeoLocation() is { IsZero: false } location)
            {
                var text = string.Create(CultureInfo.InvariantCulture, $"{location.Latitude:0.00000}, {location.Longitude:0.00000}");
                fields.Add(new MetadataField("Location", "GPS", text));
            }
        }

        foreach (var text in directories.Where(directory => directory is PngDirectory).SelectMany(directory => directory.Tags))
        {
            if (text.Type == PngDirectory.TagTextualData && text.Description is { Length: > 0 } description)
            {
                fields.Add(new MetadataField("Text", "Embedded text", description));
            }
        }

        return new ImageMetadataInfo(fields, HasPrivate(directories));
    }

    private static bool HasPrivate(IEnumerable<MetadataExtractor.Directory> directories) =>
        directories.Any(directory => directory switch
        {
            ExifIfd0Directory or ExifSubIfdDirectory or GpsDirectory or ExifThumbnailDirectory => directory.TagCount > 0,
            XmpDirectory or IptcDirectory => directory.TagCount > 0,
            PngDirectory png => png.ContainsTag(PngDirectory.TagTextualData) || png.ContainsTag(PngDirectory.TagLastModificationTime),
            _ => false,
        });

    private static void Add(
        List<MetadataField> fields,
        string group,
        string name,
        IEnumerable<MetadataExtractor.Directory> directories,
        int tag)
    {
        foreach (var directory in directories)
        {
            if (directory.ContainsTag(tag) && directory.GetDescription(tag) is { Length: > 0 } value)
            {
                fields.Add(new MetadataField(group, name, value.Trim()));
                return;
            }
        }
    }
}
