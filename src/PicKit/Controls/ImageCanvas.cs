using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PicKit.Core.Imaging;
using PicKit.Core.Rendering;
using SkiaSharp;

namespace PicKit.Controls;

/// <summary>
/// Renders the visible part of the image into a viewport-sized bitmap with Skia. The image is
/// never handed to WPF, so a huge photo costs no second copy in memory, and what appears on
/// screen comes from the same code that produces exported pixels.
/// </summary>
public sealed class ImageCanvas : FrameworkElement
{
    public static readonly DependencyProperty ImageProperty = DependencyProperty.Register(
        nameof(Image),
        typeof(ImageBuffer),
        typeof(ImageCanvas),
        new FrameworkPropertyMetadata(null, (element, _) => ((ImageCanvas)element).OnImageChanged()));

    private readonly Checkerboard _checkerboard = new(8, new SKColor(0xFF, 0xFF, 0xFF), new SKColor(0xE6, 0xE6, 0xE6));
    private WriteableBitmap? _surface;

    public ImageCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
    }

    public ImageBuffer? Image
    {
        get => (ImageBuffer?)GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    public ViewTransform View { get; } = new();

    protected override void OnRender(DrawingContext drawingContext)
    {
        var scale = VisualTreeHelper.GetDpi(this);
        var pixelWidth = (int)Math.Round(ActualWidth * scale.DpiScaleX);
        var pixelHeight = (int)Math.Round(ActualHeight * scale.DpiScaleY);
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return;
        }

        View.SetViewport(pixelWidth, pixelHeight);
        var surface = SurfaceFor(pixelWidth, pixelHeight, scale);

        surface.Lock();
        try
        {
            var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var skia = SKSurface.Create(info, surface.BackBuffer, surface.BackBufferStride))
            {
                Draw(skia.Canvas);
            }

            surface.AddDirtyRect(new Int32Rect(0, 0, pixelWidth, pixelHeight));
        }
        finally
        {
            surface.Unlock();
        }

        drawingContext.DrawImage(surface, new Rect(0, 0, ActualWidth, ActualHeight));
    }

    private void Draw(SKCanvas canvas)
    {
        canvas.Clear(SKColors.Transparent);

        var image = Image;
        if (image is null)
        {
            return;
        }

        var bounds = View.ImageToView(new SKRect(0, 0, image.Width, image.Height));
        _checkerboard.Draw(canvas, bounds);

        using var skiaImage = image.AsImage();
        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        canvas.DrawImage(skiaImage, bounds, sampling);
    }

    private WriteableBitmap SurfaceFor(int width, int height, DpiScale scale)
    {
        if (_surface is null || _surface.PixelWidth != width || _surface.PixelHeight != height)
        {
            _surface = new WriteableBitmap(
                width,
                height,
                96 * scale.DpiScaleX,
                96 * scale.DpiScaleY,
                PixelFormats.Pbgra32,
                null);
        }

        return _surface;
    }

    private void OnImageChanged()
    {
        var image = Image;
        if (image is not null)
        {
            View.SetImage(image.Width, image.Height, refit: true);
        }

        InvalidateVisual();
    }
}
