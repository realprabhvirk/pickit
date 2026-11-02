namespace PicKit.Core.Segmentation;

/// <summary>
/// Everything specific to one model, so a different one can be dropped in without touching
/// the engine or the tool. Models aren't downloaded at run time: PicKit never needs the internet.
/// </summary>
public sealed record SegmentationModel
{
    public const string DefaultFileName = "isnet-general-use.onnx";

    /// <summary>IS-Net general use, chosen by benchmarking on a CPU-only laptop. See docs/BACKGROUND-REMOVAL.md.</summary>
    public static SegmentationModel Default { get; } = new()
    {
        FileName = DefaultFileName,
        InputSize = 1024,
        Mean = [0.5f, 0.5f, 0.5f],
        Deviation = [1f, 1f, 1f],
        Sha256 = "60920e99c45464f2ba57bee2ad08c919a52bbf852739e96947fbb4358c0d964a",
    };

    public required string FileName { get; init; }

    /// <summary>The square size the network works at.</summary>
    public required int InputSize { get; init; }

    /// <summary>Per-channel normalisation in RGB order.</summary>
    public required float[] Mean { get; init; }

    public required float[] Deviation { get; init; }

    public required string Sha256 { get; init; }

    /// <summary>Looks beside the app, where the installer puts it. Returns null if it isn't there.</summary>
    public string? Locate()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "models", FileName);
        return File.Exists(path) ? path : null;
    }
}
