using PicKit.Core.Diagnostics;
using PicKit.Core.Files;
using PicKit.Core.Imaging;
using PicKit.Core.Metadata;
using SkiaSharp;

namespace PicKit.Core.Batch;

public enum BatchOperation
{
    Compress,
    Resize,
    Convert,
}

public enum BatchOutput
{
    /// <summary>Beside each original, under a new name.</summary>
    SameFolder,

    /// <summary>A new folder beside the first image.</summary>
    NewFolder,

    /// <summary>A folder the user picked.</summary>
    ChosenFolder,
}

public sealed record BatchOptions
{
    public required BatchOperation Operation { get; init; }

    /// <summary>The format to write, or Unknown to keep each image's own format.</summary>
    public ImageFormat Format { get; init; } = ImageFormat.Unknown;

    public CompressionPreset Preset { get; init; } = CompressionPreset.Balanced;

    public int Quality { get; init; } = 85;

    public ResizeMode ResizeMode { get; init; } = ResizeMode.Fit;

    public int Width { get; init; } = 1920;

    public int Height { get; init; } = 1080;

    /// <summary>In Scale mode, works out the height from the width for each image.</summary>
    public bool KeepAspect { get; init; } = true;

    public SKColor Background { get; init; } = SKColors.White;

    public BatchOutput Output { get; init; } = BatchOutput.SameFolder;

    public string? ChosenFolder { get; init; }

    public bool PreserveNames { get; init; } = true;

    /// <summary>Carries EXIF over between JPEGs when the pixels are only being recompressed.</summary>
    public bool KeepMetadata { get; init; }
}

public sealed record BatchItemResult(string Source, string? Output, long OriginalSize, long OutputSize, string? Error)
{
    public bool Succeeded => Error is null;
}

public sealed record BatchProgress(int Completed, int Total, string CurrentName);

public sealed record BatchSummary(IReadOnlyList<BatchItemResult> Results, bool WasCancelled, string? OutputFolder)
{
    public int Succeeded => Results.Count(result => result.Succeeded);

    public int Failed => Results.Count(result => !result.Succeeded);
}

/// <summary>
/// Runs one operation over many images. Each image stands alone: a file that can't be read is
/// reported and the rest carry on. Originals are never overwritten, and cancelling leaves only
/// complete files behind.
/// </summary>
public static class BatchRunner
{
    private const int MaxParallel = 4;
    private const string NewFolderName = "PicKit";

    public static async Task<BatchSummary> RunAsync(
        IReadOnlyList<string> paths,
        BatchOptions options,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var folder = OutputFolderFor(paths, options);
        var results = new BatchItemResult[paths.Count];
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var completed = 0;
        var cancelled = false;

        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 1, 1, MaxParallel) };
        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(0, paths.Count),
                parallel,
                (index, _) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var source = paths[index];
                    results[index] = ProcessOne(source, index, paths, options, folder, reserved);

                    var done = Interlocked.Increment(ref completed);
                    progress?.Report(new BatchProgress(done, paths.Count, Path.GetFileName(source)));
                    return ValueTask.CompletedTask;
                });
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        return new BatchSummary(results.Where(result => result is not null).ToList(), cancelled, folder);
    }

    private static BatchItemResult ProcessOne(
        string source, int index, IReadOnlyList<string> all, BatchOptions options, string? folder, HashSet<string> reserved)
    {
        long originalSize = 0;
        string? target = null;
        try
        {
            var original = File.ReadAllBytes(source);
            originalSize = original.LongLength;

            using var decoded = ImageDecoder.Decode(original);
            var format = OutputFormat(decoded.Format, options);
            target = ReserveOutput(source, index, all, options, folder, format, reserved);

            using var pixels = Transform(decoded.Pixels, options);
            var encode = new EncodeOptions(format, QualityFor(format, options), options.Background);
            var data = ImageEncoder.Encode(pixels, encode);

            if (options.KeepMetadata && options.Operation != BatchOperation.Resize
                && format == ImageFormat.Jpeg && decoded.Format == ImageFormat.Jpeg
                && ExifCarryOver.ExtractUprightExif(original) is { } exif)
            {
                data = ExifCarryOver.Insert(data, exif);
            }

            var result = ExportService.WriteBytes(data, target);
            return new BatchItemResult(source, result.Path, originalSize, result.Size, null);
        }
        catch (Exception ex) when (ex is ImageException or IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Batch item failed: {source}", ex);
            Release(target, reserved);
            return new BatchItemResult(source, null, originalSize, 0, FriendlyError(ex));
        }
    }

    private static ImageBuffer Transform(ImageBuffer pixels, BatchOptions options)
    {
        if (options.Operation != BatchOperation.Resize)
        {
            return pixels.Clone();
        }

        var height = options.ResizeMode == ResizeMode.Scale && options.KeepAspect
            ? ResizeCalculator.HeightForWidth(pixels.Width, pixels.Height, options.Width)
            : options.Height;

        var plan = ResizeCalculator.Plan(pixels.Width, pixels.Height, options.Width, height, options.ResizeMode);
        return ImageOps.ApplyPlan(pixels, plan);
    }

    private static ImageFormat OutputFormat(ImageFormat source, BatchOptions options)
    {
        if (options.Format != ImageFormat.Unknown)
        {
            return options.Format;
        }

        return ImageFormats.CanEncode(source) ? source : ImageFormat.Png;
    }

    private static int QualityFor(ImageFormat format, BatchOptions options) =>
        options.Operation == BatchOperation.Compress ? Compression.QualityFor(format, options.Preset) : options.Quality;

    private static string? OutputFolderFor(IReadOnlyList<string> paths, BatchOptions options)
    {
        switch (options.Output)
        {
            case BatchOutput.ChosenFolder:
                return options.ChosenFolder;
            case BatchOutput.NewFolder when paths.Count > 0:
                var parent = Path.GetDirectoryName(paths[0]) ?? string.Empty;
                return UniqueFolder(Path.Combine(parent, $"{NewFolderName} {options.Operation}"));
            default:
                return null;
        }
    }

    private static string UniqueFolder(string path)
    {
        var candidate = path;
        for (var attempt = 2; Directory.Exists(candidate); attempt++)
        {
            candidate = $"{path} ({attempt})";
        }

        return candidate;
    }

    private static string ReserveOutput(
        string source, int index, IReadOnlyList<string> all, BatchOptions options, string? folder, ImageFormat format, HashSet<string> reserved)
    {
        var extension = format == ImageFormats.FromExtension(source) ? Path.GetExtension(source) : ImageFormats.Extension(format);
        var directory = folder ?? Path.GetDirectoryName(source) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(source);

        string name;
        if (!options.PreserveNames)
        {
            name = $"{BaseName(all[0])}-{index + 1:000}{extension}";
        }
        else if (folder is null)
        {
            // In the same folder, the same name would be the original.
            name = $"{stem}-{SuffixFor(options)}{extension}";
        }
        else
        {
            name = stem + extension;
        }

        lock (reserved)
        {
            var path = FileNames.MakeUnique(
                Path.Combine(directory, name),
                candidate => reserved.Contains(candidate) || File.Exists(candidate));

            reserved.Add(path);
            return path;
        }
    }

    private static void Release(string? path, HashSet<string> reserved)
    {
        if (path is not null)
        {
            lock (reserved)
            {
                reserved.Remove(path);
            }
        }
    }

    private static string SuffixFor(BatchOptions options) => options.Operation switch
    {
        BatchOperation.Compress => "compressed",
        BatchOperation.Resize => "resized",
        _ => ImageFormats.DisplayName(options.Format).ToLowerInvariant(),
    };

    /// <summary>"holiday 12" becomes "holiday", so numbered output doesn't read "holiday 12-001".</summary>
    private static string BaseName(string path)
    {
        var stem = Path.GetFileNameWithoutExtension(path).TrimEnd("0123456789 _-".ToCharArray());
        return stem.Length > 0 ? stem : "image";
    }

    private static string FriendlyError(Exception exception) => exception switch
    {
        ImageException image => image.Message.Split('\n')[0],
        UnauthorizedAccessException => "PicKit doesn't have permission to use this file.",
        _ => "The file couldn't be read or written.",
    };
}
