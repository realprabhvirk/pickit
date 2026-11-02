using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using PicKit.Core.Imaging;
using SkiaSharp;

namespace PicKit.Core.Segmentation;

/// <summary>
/// Finds the main subject of an image, entirely on this PC. The network is loaded when first
/// needed and released with this object, because it holds far more memory than the rest of the
/// app, and reloading takes only a second.
/// </summary>
public sealed class BackgroundSegmenter : IDisposable
{
    /// <summary>Alpha this low is haze, and this high is solid. The network rarely reaches either end.</summary>
    private const int HazeFloor = 16;
    private const int SolidCeiling = 235;

    private readonly SegmentationModel _model;
    private readonly string _modelPath;
    private readonly object _gate = new();
    private InferenceSession? _session;
    private RunOptions? _activeRun;

    public BackgroundSegmenter(SegmentationModel model, string modelPath)
    {
        _model = model;
        _modelPath = modelPath;
    }

    /// <summary>
    /// Returns a mask the size of the image, where each pixel's alpha is how much of the image
    /// to keep. The caller owns it. Blocks for a few seconds, so call it off the UI thread.
    /// </summary>
    public ImageBuffer CreateMask(ImageBuffer image, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var tensor = Preprocess(image, _model);
        cancellationToken.ThrowIfCancellationRequested();

        var session = EnsureSession();
        var inputName = session.InputMetadata.Keys.First();

        using var run = new RunOptions();
        lock (_gate)
        {
            _activeRun = run;
        }

        using var cancelRun = cancellationToken.Register(() => Terminate(run));
        try
        {
            using var results = session.Run([NamedOnnxValue.CreateFromTensor(inputName, tensor)], session.OutputMetadata.Keys.ToList(), run);
            using var small = ToMask(results.First().AsTensor<float>(), _model.InputSize);
            return ImageOps.Resize(small, image.Width, image.Height);
        }
        catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OnnxRuntimeException ex)
        {
            throw new ImageException(
                ImageErrorKind.Unreadable,
                "PicKit couldn't work out the background of this image.",
                ex.Message,
                ex);
        }
        finally
        {
            lock (_gate)
            {
                _activeRun = null;
            }
        }
    }

    /// <summary>Scales to the network's size and normalises, as a planar RGB tensor.</summary>
    public static DenseTensor<float> Preprocess(ImageBuffer image, SegmentationModel model)
    {
        var size = model.InputSize;

        // The network expects a solid picture, so any existing transparency sits on white.
        using var flat = ImageOps.Flatten(image, SKColors.White);
        using var scaled = ImageOps.Resize(flat, size, size);

        var tensor = new DenseTensor<float>([1, 3, size, size]);
        var values = tensor.Buffer.Span;
        var pixels = scaled.Pixels;
        var plane = size * size;
        for (var i = 0; i < plane; i++)
        {
            var pixel = pixels[i];
            values[i] = ((((pixel >> 16) & 0xFF) / 255f) - model.Mean[0]) / model.Deviation[0];
            values[plane + i] = ((((pixel >> 8) & 0xFF) / 255f) - model.Mean[1]) / model.Deviation[1];
            values[(2 * plane) + i] = (((pixel & 0xFF) / 255f) - model.Mean[2]) / model.Deviation[2];
        }

        return tensor;
    }

    /// <summary>Turns raw network output into alpha, stretched to full range with faint haze removed.</summary>
    public static ImageBuffer ToMask(Tensor<float> output, int size)
    {
        var values = new float[size * size];
        var (low, high) = (float.MaxValue, float.MinValue);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var value = output[0, 0, y, x];
                values[(y * size) + x] = value;
                low = Math.Min(low, value);
                high = Math.Max(high, value);
            }
        }

        var range = Math.Max(high - low, 1e-6f);
        var mask = ImageBuffer.Create(size, size);
        var pixels = mask.Pixels;
        for (var i = 0; i < values.Length; i++)
        {
            pixels[i] = MaskPixel(Levels((values[i] - low) / range));
        }

        return mask;
    }

    /// <summary>A mask pixel holding this much "keep", as premultiplied white.</summary>
    public static uint MaskPixel(byte alpha) => ((uint)alpha << 24) | ((uint)alpha << 16) | ((uint)alpha << 8) | alpha;

    public void Dispose()
    {
        lock (_gate)
        {
            _activeRun?.Terminate = true;
            _session?.Dispose();
            _session = null;
        }
    }

    private static byte Levels(float value)
    {
        var stretched = ((value * 255) - HazeFloor) * 255 / (SolidCeiling - HazeFloor);
        return (byte)Math.Clamp(MathF.Round(stretched), 0, 255);
    }

    private void Terminate(RunOptions run)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_activeRun, run))
            {
                run.Terminate = true;
            }
        }
    }

    private InferenceSession EnsureSession()
    {
        lock (_gate)
        {
            if (_session is null)
            {
                using var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };

                // Waiting threads sleep instead of spinning, leaving the CPU to the rest of the PC.
                options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
                _session = new InferenceSession(_modelPath, options);
            }

            return _session;
        }
    }
}
