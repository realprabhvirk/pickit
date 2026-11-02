using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using PicKit.Core.Documents;
using PicKit.Core.Imaging;
using PicKit.Core.Markup;
using PicKit.Core.Rendering;
using SkiaSharp;

namespace PicKit.Controls;

/// <summary>
/// Renders the visible part of the image into a viewport-sized bitmap with Skia. The image is
/// never handed to WPF, so a huge photo costs no second full copy in memory, and what appears
/// on screen comes from the same drawing code that produces exported pixels.
/// </summary>
public sealed class ImageCanvas : FrameworkElement
{
    public static readonly DependencyProperty DocumentProperty = DependencyProperty.Register(
        nameof(Document),
        typeof(EditorDocument),
        typeof(ImageCanvas),
        new FrameworkPropertyMetadata(null, (element, change) =>
            ((ImageCanvas)element).OnDocumentChanged((EditorDocument?)change.OldValue, (EditorDocument?)change.NewValue)));

    public static readonly DependencyProperty ToolProperty = DependencyProperty.Register(
        nameof(Tool),
        typeof(ICanvasTool),
        typeof(ImageCanvas),
        new FrameworkPropertyMetadata(null, (element, _) => ((ImageCanvas)element).OnToolChanged()));

    private static readonly DependencyPropertyKey ZoomTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(ZoomText),
        typeof(string),
        typeof(ImageCanvas),
        new PropertyMetadata("100%"));

    public static readonly DependencyProperty ZoomTextProperty = ZoomTextPropertyKey.DependencyProperty;

    private const double WheelStep = 1.2;
    private const double NearestNeighbourFromZoom = 3;

    private static readonly double[] ZoomStops =
        [0.05, 0.1, 0.25, 0.33, 0.5, 0.67, 0.75, 1, 1.5, 2, 3, 4, 6, 8, 12, 16, 32];

    private Checkerboard? _checkerboard;
    private WriteableBitmap? _surface;
    private DisplayPyramid? _pyramid;
    private bool _isPanning;
    private bool _isToolGestureActive;
    private Point _panOrigin;

    public ImageCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
        FocusVisualStyle = null;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);

        ZoomInCommand = new RelayCommand(() => StepZoom(1));
        ZoomOutCommand = new RelayCommand(() => StepZoom(-1));
        FitCommand = new RelayCommand(FitToWindow);
        ActualSizeCommand = new RelayCommand(ShowActualSize);
    }

    public EditorDocument? Document
    {
        get => (EditorDocument?)GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public ICanvasTool? Tool
    {
        get => (ICanvasTool?)GetValue(ToolProperty);
        set => SetValue(ToolProperty, value);
    }

    public string ZoomText
    {
        get => (string)GetValue(ZoomTextProperty);
        private set => SetValue(ZoomTextPropertyKey, value);
    }

    public ViewTransform View { get; } = new();

    /// <summary>The visible part of the image moved, or the image itself changed.</summary>
    public event Action? ViewChanged;

    public ICommand ZoomInCommand { get; }

    public ICommand ZoomOutCommand { get; }

    public ICommand FitCommand { get; }

    public ICommand ActualSizeCommand { get; }

    /// <summary>True while the space bar is held, which turns a left drag into a pan.</summary>
    public bool IsPanModifierHeld { get; set; }

    private ImageBuffer? Image => Document?.Pixels;

    private Cursor? IdleCursor => Image is not null && Tool is null ? Cursors.Hand : null;

    public void Redraw() => InvalidateVisual();

    public void ScrollTo(double horizontal, double vertical)
    {
        View.ScrollTo(horizontal, vertical);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        // Until an image is open there's nothing to draw, so the window paints without Skia.
        if (Image is null)
        {
            return;
        }

        var scale = VisualTreeHelper.GetDpi(this);
        var pixelWidth = (int)Math.Round(ActualWidth * scale.DpiScaleX);
        var pixelHeight = (int)Math.Round(ActualHeight * scale.DpiScaleY);
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return;
        }

        View.DeviceScale = (float)scale.DpiScaleX;
        View.SetViewport(pixelWidth, pixelHeight);
        UpdateZoomText();
        ViewChanged?.Invoke();

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

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        e.Handled = WheelAt(e.GetPosition(this), e.Delta);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (PressAt(e.GetPosition(this), e.ChangedButton, Keyboard.Modifiers))
        {
            CaptureMouse();
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        MoveTo(e.GetPosition(this), Keyboard.Modifiers);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Tool?.OnPointerLeft();
        InvalidateVisual();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (ReleaseAt(e.GetPosition(this), e.ChangedButton, Keyboard.Modifiers))
        {
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    /// <summary>Zooms around a point in this element's coordinates.</summary>
    public bool WheelAt(Point position, int delta)
    {
        if (Image is null)
        {
            return false;
        }

        var factor = Math.Pow(WheelStep, delta / (double)Mouse.MouseWheelDeltaForOneLine);
        ZoomTo(View.Zoom * factor, ToViewPoint(position));
        return true;
    }

    /// <summary>Starts a pan or a tool gesture. Returns true if the gesture was taken.</summary>
    public bool PressAt(Point position, MouseButton button, ModifierKeys modifiers)
    {
        if (Image is null)
        {
            return false;
        }

        if (button == MouseButton.Middle || (button == MouseButton.Left && (IsPanModifierHeld || Tool is null)))
        {
            _isPanning = true;
            _panOrigin = position;
            Cursor = Cursors.SizeAll;
            return true;
        }

        if (button == MouseButton.Left && Tool is { } tool)
        {
            _isToolGestureActive = true;
            tool.OnPointerDown(PointerAt(position, modifiers));
            InvalidateVisual();
            return true;
        }

        return false;
    }

    public void MoveTo(Point position, ModifierKeys modifiers)
    {
        if (_isPanning)
        {
            var scale = VisualTreeHelper.GetDpi(this);
            View.PanBy((position.X - _panOrigin.X) * scale.DpiScaleX, (position.Y - _panOrigin.Y) * scale.DpiScaleY);
            _panOrigin = position;
            InvalidateVisual();
        }
        else if (Tool is { } tool && Image is not null)
        {
            var pointer = PointerAt(position, modifiers);
            tool.OnPointerMove(pointer, _isToolGestureActive);
            Cursor = tool.CursorAt(pointer);
            InvalidateVisual();
        }
    }

    public bool ReleaseAt(Point position, MouseButton button, ModifierKeys modifiers)
    {
        if (_isPanning && button is MouseButton.Left or MouseButton.Middle)
        {
            _isPanning = false;
            Cursor = IdleCursor;
            return true;
        }

        if (button == MouseButton.Left && _isToolGestureActive)
        {
            _isToolGestureActive = false;
            Tool?.OnPointerUp(PointerAt(position, modifiers));
            InvalidateVisual();
            return true;
        }

        return false;
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
        _checkerboard ??= new Checkerboard(8, new SKColor(0xFF, 0xFF, 0xFF), new SKColor(0xE3, 0xE3, 0xE3));
        _checkerboard.Draw(canvas, bounds);

        var source = _pyramid?.ForZoom(View.Zoom) ?? image;
        using var skiaImage = source.AsImage();
        var filter = View.Zoom >= NearestNeighbourFromZoom ? SKFilterMode.Nearest : SKFilterMode.Linear;
        var sampling = new SKSamplingOptions(filter);
        if (Tool?.DrawImage(canvas, skiaImage, bounds, sampling, View.Zoom) != true)
        {
            canvas.DrawImage(skiaImage, bounds, sampling);
        }

        canvas.Save();
        canvas.ClipRect(bounds);
        canvas.Concat(View.Matrix);
        MarkupRenderer.Draw(canvas, Document!.Markups);
        Tool?.DrawOnImage(canvas);
        canvas.Restore();

        Tool?.DrawOverlay(canvas, View);
    }

    private WriteableBitmap SurfaceFor(int width, int height, DpiScale scale)
    {
        if (_surface is null
            || _surface.PixelWidth != width
            || _surface.PixelHeight != height
            || Math.Abs(_surface.DpiX - (96 * scale.DpiScaleX)) > 0.01)
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

    private void OnDocumentChanged(EditorDocument? previous, EditorDocument? current)
    {
        if (previous is not null)
        {
            previous.Changed -= OnDocumentContentChanged;
        }

        if (current is not null)
        {
            current.Changed += OnDocumentContentChanged;
        }

        RebuildForImage(refit: true);
    }

    private void OnDocumentContentChanged(DocumentChange change)
    {
        switch (change.Kind)
        {
            case DocumentChangeKind.Replaced:
                var image = Image;
                var sizeChanged = image is null || image.Width != View.ImageWidth || image.Height != View.ImageHeight;
                RebuildForImage(sizeChanged);
                break;
            case DocumentChangeKind.Region:
                _pyramid?.Update(change.Region);
                InvalidateVisual();
                break;
            default:
                InvalidateVisual();
                break;
        }
    }

    private void RebuildForImage(bool refit)
    {
        _pyramid?.Dispose();
        _pyramid = null;

        if (Image is { } image)
        {
            _pyramid = new DisplayPyramid(image);
            View.SetImage(image.Width, image.Height, refit || View.IsFitted);
            UpdateZoomText();
        }

        Cursor = IdleCursor;
        ViewChanged?.Invoke();
        InvalidateVisual();
    }

    private void OnToolChanged()
    {
        Cursor = IdleCursor;
        InvalidateVisual();
    }

    private void FitToWindow()
    {
        View.Fit();
        UpdateZoomText();
        InvalidateVisual();
    }

    private void ShowActualSize()
    {
        View.ActualSize();
        UpdateZoomText();
        InvalidateVisual();
    }

    private void StepZoom(int direction)
    {
        var current = View.Zoom;
        var next = direction > 0
            ? ZoomStops.FirstOrDefault(stop => stop > current * 1.001, ViewTransform.MaxZoom)
            : ZoomStops.LastOrDefault(stop => stop < current * 0.999, ViewTransform.MinZoom);

        ZoomTo(next, new SKPoint((float)(View.ViewportWidth / 2), (float)(View.ViewportHeight / 2)));
    }

    private void ZoomTo(double zoom, SKPoint anchor)
    {
        View.ZoomAt(anchor, zoom);
        UpdateZoomText();
        InvalidateVisual();
    }

    private void UpdateZoomText() => ZoomText = $"{View.Zoom * 100:0}%";

    private CanvasPointer PointerAt(Point position, ModifierKeys modifiers)
    {
        var view = ToViewPoint(position);
        return new CanvasPointer(view, View.ViewToImage(view), modifiers, (float)View.Zoom);
    }

    private SKPoint ToViewPoint(Point position)
    {
        var scale = VisualTreeHelper.GetDpi(this);
        return new SKPoint((float)(position.X * scale.DpiScaleX), (float)(position.Y * scale.DpiScaleY));
    }
}
