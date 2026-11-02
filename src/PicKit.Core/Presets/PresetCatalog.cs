using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PicKit.Core.Presets;

public sealed record PlatformPreset
{
    public required string Name { get; init; }

    /// <summary>The heading this preset is listed under.</summary>
    public string Group { get; init; } = string.Empty;

    public required int Width { get; init; }

    public required int Height { get; init; }

    /// <summary>A size to stay within, rather than a shape the image must be cut to.</summary>
    public bool FitOnly { get; init; }

    public string? Note { get; init; }

    /// <summary>Where the numbers came from, kept so they can be rechecked.</summary>
    public string? Source { get; init; }

    public string DimensionsText => $"{Width} × {Height}";

    public string AspectText
    {
        get
        {
            var divisor = GreatestCommonDivisor(Width, Height);
            var (ratioWidth, ratioHeight) = (Width / divisor, Height / divisor);

            // Some real-world sizes are only approximately a neat ratio.
            return ratioWidth > 32 ? $"{(double)Width / Height:0.00}:1" : $"{ratioWidth}:{ratioHeight}";
        }
    }

    private static int GreatestCommonDivisor(int a, int b) => b == 0 ? a : GreatestCommonDivisor(b, a % b);
}

public sealed record PresetGroup
{
    public required string Name { get; init; }

    public required IReadOnlyList<PlatformPreset> Presets { get; init; }
}

public sealed record PresetCatalog
{
    public required string Verified { get; init; }

    public required IReadOnlyList<PresetGroup> Groups { get; init; }

    /// <summary>Platform sizes change, so they live in a data file rather than in code.</summary>
    public static PresetCatalog Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PicKit.Core.Presets.presets.json")
            ?? throw new InvalidOperationException("The preset list is missing from the build.");

        var catalog = JsonSerializer.Deserialize(stream, PresetJsonContext.Default.PresetCatalog)
            ?? throw new InvalidOperationException("The preset list is empty.");

        return catalog with
        {
            Groups =
            [
                .. catalog.Groups.Select(group => group with
                {
                    Presets = [.. group.Presets.Select(preset => preset with { Group = group.Name })],
                }),
            ],
        };
    }
}

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(PresetCatalog))]
internal sealed partial class PresetJsonContext : JsonSerializerContext;
