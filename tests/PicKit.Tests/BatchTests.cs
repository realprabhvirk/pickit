using PicKit.Core.Batch;
using PicKit.Core.Imaging;
using PicKit.Core.Metadata;
using SkiaSharp;

namespace PicKit.Tests;

public class BatchTests
{
    private static List<string> CreateImages(string folder, int count, ImageFormat format = ImageFormat.Png, int width = 200, int height = 100)
    {
        var paths = new List<string>();
        for (var i = 1; i <= count; i++)
        {
            var path = Path.Combine(folder, $"photo{i}{ImageFormats.Extension(format)}");
            using var image = TestImages.Photo(width, height, seed: i);
            File.WriteAllBytes(path, ImageEncoder.Encode(image, new EncodeOptions(format, 95)));
            paths.Add(path);
        }

        return paths;
    }

    [Fact]
    public async Task Compress_writes_named_copies_and_leaves_originals()
    {
        var folder = TestImages.TempFolder();
        var paths = CreateImages(folder, 3, ImageFormat.Jpeg, 400, 300);
        var before = File.ReadAllBytes(paths[0]);

        var summary = await BatchRunner.RunAsync(paths, new BatchOptions { Operation = BatchOperation.Compress, Preset = CompressionPreset.Small });

        Assert.Equal(3, summary.Succeeded);
        Assert.Equal(before, File.ReadAllBytes(paths[0]));
        Assert.All(summary.Results, result =>
        {
            Assert.EndsWith("-compressed.jpg", result.Output);
            Assert.True(result.OutputSize < result.OriginalSize);
        });
    }

    [Fact]
    public async Task Resize_fit_keeps_every_image_inside_the_box()
    {
        var folder = TestImages.TempFolder();
        var paths = CreateImages(folder, 2);

        var summary = await BatchRunner.RunAsync(paths, new BatchOptions
        {
            Operation = BatchOperation.Resize,
            ResizeMode = ResizeMode.Fit,
            Width = 100,
            Height = 100,
        });

        using var result = ImageDecoder.Decode(summary.Results[0].Output!);
        Assert.Equal((100, 50), (result.Pixels.Width, result.Pixels.Height));
    }

    [Fact]
    public async Task Scale_with_keep_aspect_follows_each_images_own_shape()
    {
        var folder = TestImages.TempFolder();
        var wide = CreateImages(folder, 1, width: 400, height: 100);
        var tall = Path.Combine(folder, "tall.png");
        using (var image = TestImages.Photo(100, 400))
        {
            File.WriteAllBytes(tall, ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png)));
        }

        var summary = await BatchRunner.RunAsync([wide[0], tall], new BatchOptions
        {
            Operation = BatchOperation.Resize,
            ResizeMode = ResizeMode.Scale,
            Width = 200,
        });

        using var first = ImageDecoder.Decode(summary.Results[0].Output!);
        using var second = ImageDecoder.Decode(summary.Results[1].Output!);
        Assert.Equal(50, first.Pixels.Height);
        Assert.Equal(800, second.Pixels.Height);
    }

    [Fact]
    public async Task Convert_changes_the_format_and_extension()
    {
        var folder = TestImages.TempFolder();
        var paths = CreateImages(folder, 2);

        var summary = await BatchRunner.RunAsync(paths, new BatchOptions
        {
            Operation = BatchOperation.Convert,
            Format = ImageFormat.WebP,
            Quality = 85,
        });

        using var decoded = ImageDecoder.Decode(summary.Results[0].Output!);
        Assert.Equal(ImageFormat.WebP, decoded.Format);
        Assert.EndsWith("photo1-webp.webp", summary.Results[0].Output);
    }

    [Fact]
    public async Task Converting_transparency_to_jpeg_uses_the_chosen_background()
    {
        var folder = TestImages.TempFolder();
        var path = Path.Combine(folder, "logo.png");
        using (var image = ImageBuffer.Create(32, 32))
        {
            File.WriteAllBytes(path, ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png)));
        }

        var summary = await BatchRunner.RunAsync([path], new BatchOptions
        {
            Operation = BatchOperation.Convert,
            Format = ImageFormat.Jpeg,
            Background = SKColors.White,
        });

        using var decoded = ImageDecoder.Decode(summary.Results[0].Output!);
        Assert.True((decoded.Pixels.Pixels[0] & 0xFF) > 250);
    }

    [Fact]
    public async Task A_bad_file_is_reported_and_the_rest_carry_on()
    {
        var folder = TestImages.TempFolder();
        var paths = CreateImages(folder, 3);
        var broken = Path.Combine(folder, "broken.png");
        File.WriteAllBytes(broken, [1, 2, 3, 4, 5]);
        var missing = Path.Combine(folder, "missing.png");
        paths.Insert(1, broken);
        paths.Add(missing);

        var summary = await BatchRunner.RunAsync(paths, new BatchOptions { Operation = BatchOperation.Compress });

        Assert.Equal(3, summary.Succeeded);
        Assert.Equal(2, summary.Failed);
        var failure = summary.Results.Single(result => result.Source == broken);
        Assert.Contains("couldn't open", failure.Error);
        Assert.Null(failure.Output);
    }

    [Fact]
    public async Task Existing_files_are_never_overwritten()
    {
        var folder = TestImages.TempFolder();
        var paths = CreateImages(folder, 1, ImageFormat.Jpeg);
        var taken = Path.Combine(folder, "photo1-compressed.jpg");
        File.WriteAllBytes(taken, [9, 9, 9]);

        var summary = await BatchRunner.RunAsync(paths, new BatchOptions { Operation = BatchOperation.Compress });

        Assert.Equal(new byte[] { 9, 9, 9 }, File.ReadAllBytes(taken));
        Assert.EndsWith("photo1-compressed (2).jpg", summary.Results[0].Output);
    }

    [Fact]
    public async Task Preserved_names_go_into_a_new_folder_that_is_created_for_them()
    {
        var folder = TestImages.TempFolder();
        var paths = CreateImages(folder, 2);

        var summary = await BatchRunner.RunAsync(paths, new BatchOptions
        {
            Operation = BatchOperation.Compress,
            Output = BatchOutput.NewFolder,
        });

        Assert.Equal(Path.Combine(folder, "PicKit Compress"), summary.OutputFolder);
        Assert.True(File.Exists(Path.Combine(summary.OutputFolder!, "photo1.png")));
    }

    [Fact]
    public async Task A_second_run_gets_its_own_new_folder()
    {
        var folder = TestImages.TempFolder();
        var paths = CreateImages(folder, 1);
        var options = new BatchOptions { Operation = BatchOperation.Compress, Output = BatchOutput.NewFolder };

        await BatchRunner.RunAsync(paths, options);
        var second = await BatchRunner.RunAsync(paths, options);

        Assert.Equal(Path.Combine(folder, "PicKit Compress (2)"), second.OutputFolder);
    }

    [Fact]
    public async Task Chosen_folder_is_used()
    {
        var folder = TestImages.TempFolder();
        var target = TestImages.TempFolder();
        var paths = CreateImages(folder, 2);

        var summary = await BatchRunner.RunAsync(paths, new BatchOptions
        {
            Operation = BatchOperation.Compress,
            Output = BatchOutput.ChosenFolder,
            ChosenFolder = target,
        });

        Assert.Equal(2, Directory.GetFiles(target).Length);
        Assert.Equal(2, summary.Succeeded);
    }

    [Fact]
    public async Task Same_names_from_different_folders_do_not_collide()
    {
        var first = TestImages.TempFolder();
        var second = TestImages.TempFolder();
        var target = TestImages.TempFolder();
        var paths = CreateImages(first, 1).Concat(CreateImages(second, 1)).ToList();

        var summary = await BatchRunner.RunAsync(paths, new BatchOptions
        {
            Operation = BatchOperation.Compress,
            Output = BatchOutput.ChosenFolder,
            ChosenFolder = target,
        });

        Assert.Equal(2, summary.Succeeded);
        Assert.Equal(2, Directory.GetFiles(target).Length);
    }

    [Fact]
    public async Task Without_preserved_names_files_are_numbered()
    {
        var folder = TestImages.TempFolder();
        var target = TestImages.TempFolder();
        var paths = CreateImages(folder, 3);

        await BatchRunner.RunAsync(paths, new BatchOptions
        {
            Operation = BatchOperation.Compress,
            Output = BatchOutput.ChosenFolder,
            ChosenFolder = target,
            PreserveNames = false,
        });

        var names = Directory.GetFiles(target).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(["photo-001.png", "photo-002.png", "photo-003.png"], names);
    }

    [Fact]
    public async Task Progress_reports_every_image_up_to_the_total()
    {
        var folder = TestImages.TempFolder();
        var paths = CreateImages(folder, 5);
        var seen = new List<BatchProgress>();
        var lockedProgress = new SynchronousProgress(update =>
        {
            lock (seen)
            {
                seen.Add(update);
            }
        });

        await BatchRunner.RunAsync(paths, new BatchOptions { Operation = BatchOperation.Compress }, lockedProgress);

        Assert.Equal(5, seen.Count);
        Assert.Contains(seen, update => update.Completed == 5 && update.Total == 5);
    }

    [Fact]
    public async Task Cancelling_stops_early_and_leaves_only_complete_files()
    {
        var folder = TestImages.TempFolder();
        var target = TestImages.TempFolder();
        var paths = CreateImages(folder, 40, width: 400, height: 300);
        using var cancellation = new CancellationTokenSource();
        var progress = new SynchronousProgress(update =>
        {
            if (update.Completed >= 3)
            {
                cancellation.Cancel();
            }
        });

        var summary = await BatchRunner.RunAsync(
            paths,
            new BatchOptions { Operation = BatchOperation.Compress, Output = BatchOutput.ChosenFolder, ChosenFolder = target },
            progress,
            cancellation.Token);

        Assert.True(summary.WasCancelled);
        Assert.True(summary.Results.Count < 40);
        Assert.Empty(Directory.GetFiles(target, "*.tmp"));
        Assert.All(Directory.GetFiles(target), file =>
        {
            using var decoded = ImageDecoder.Decode(file);
            Assert.Equal(400, decoded.Pixels.Width);
        });
    }

    [Fact]
    public async Task Metadata_is_dropped_by_default_and_kept_when_asked()
    {
        var folder = TestImages.TempFolder();
        var path = Path.Combine(folder, "photo.jpg");
        using (var image = TestImages.Photo(120, 80))
        {
            File.WriteAllBytes(path, ExifFixture.WithJpegMetadata(ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Jpeg))));
        }

        var dropped = await BatchRunner.RunAsync([path], new BatchOptions { Operation = BatchOperation.Compress });
        var kept = await BatchRunner.RunAsync([path], new BatchOptions { Operation = BatchOperation.Compress, KeepMetadata = true });

        Assert.False(ImageMetadataInfo.Read(dropped.Results[0].Output!).HasPrivateData);
        Assert.True(ImageMetadataInfo.Read(kept.Results[0].Output!).HasPrivateData);
    }

    [Fact]
    public async Task Fifty_images_complete_correctly()
    {
        var folder = TestImages.TempFolder();
        var paths = CreateImages(folder, 50, width: 300, height: 200);

        var summary = await BatchRunner.RunAsync(paths, new BatchOptions { Operation = BatchOperation.Compress });

        Assert.Equal(50, summary.Succeeded);
        Assert.Equal(50, summary.Results.Select(result => result.Output).Distinct().Count());
    }

    private sealed class SynchronousProgress(Action<BatchProgress> report) : IProgress<BatchProgress>
    {
        public void Report(BatchProgress value) => report(value);
    }
}
