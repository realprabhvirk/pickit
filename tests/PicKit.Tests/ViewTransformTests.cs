using PicKit.Core.Rendering;
using SkiaSharp;

namespace PicKit.Tests;

public class ViewTransformTests
{
    private static ViewTransform Create(int imageWidth = 4000, int imageHeight = 3000, double viewWidth = 1000, double viewHeight = 800)
    {
        var view = new ViewTransform();
        view.SetViewport(viewWidth, viewHeight);
        view.SetImage(imageWidth, imageHeight, refit: true);
        return view;
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4000, 3000)]
    [InlineData(1234.5f, 987.25f)]
    public void Image_to_view_and_back_is_lossless(float x, float y)
    {
        var view = Create();
        view.ZoomAt(new SKPoint(300, 200), 1.7);

        var back = view.ViewToImage(view.ImageToView(new SKPoint(x, y)));

        Assert.Equal(x, back.X, 0.01f);
        Assert.Equal(y, back.Y, 0.01f);
    }

    [Fact]
    public void Point_conversions_agree_with_the_render_matrix()
    {
        var view = Create();
        view.ZoomAt(new SKPoint(123, 456), 0.37);
        view.PanBy(11.4, -7.8);

        var viaMatrix = view.Matrix.MapPoint(new SKPoint(777, 555));
        var viaMethod = view.ImageToView(new SKPoint(777, 555));

        Assert.Equal(viaMatrix.X, viaMethod.X, 0.01f);
        Assert.Equal(viaMatrix.Y, viaMethod.Y, 0.01f);
    }

    [Fact]
    public void Zooming_keeps_the_point_under_the_cursor_still()
    {
        var view = Create();
        view.ZoomAt(new SKPoint(500, 400), 1);
        var anchor = new SKPoint(420, 310);
        var under = view.ViewToImage(anchor);

        view.ZoomAt(anchor, 2);

        var after = view.ViewToImage(anchor);
        Assert.Equal(under.X, after.X, 1.0f);
        Assert.Equal(under.Y, after.Y, 1.0f);
    }

    [Fact]
    public void Fit_shows_the_whole_image_inside_the_padding()
    {
        var view = Create();

        var bounds = view.ImageToView(new SKRect(0, 0, 4000, 3000));

        Assert.True(bounds.Left >= view.Padding - 1 && bounds.Top >= view.Padding - 1);
        Assert.True(bounds.Right <= 1000 - view.Padding + 1 && bounds.Bottom <= 800 - view.Padding + 1);
    }

    [Fact]
    public void Fit_never_enlarges_a_small_image()
    {
        var view = Create(imageWidth: 64, imageHeight: 64);

        Assert.Equal(1, view.Zoom);
    }

    [Fact]
    public void Smaller_than_viewport_stays_centred_when_panned()
    {
        var view = Create(imageWidth: 100, imageHeight: 100);

        view.PanBy(500, 500);

        var bounds = view.ImageToView(new SKRect(0, 0, 100, 100));
        Assert.Equal(450, bounds.Left, 1);
        Assert.Equal(350, bounds.Top, 1);
    }

    [Fact]
    public void Pan_cannot_lose_the_image_off_screen()
    {
        var view = Create();
        view.ZoomAt(new SKPoint(500, 400), 2);

        view.PanBy(100_000, 100_000);
        var bounds = view.ImageToView(new SKRect(0, 0, 4000, 3000));
        Assert.True(bounds.Left <= view.Padding + 1 && bounds.Top <= view.Padding + 1);

        view.PanBy(-100_000, -100_000);
        bounds = view.ImageToView(new SKRect(0, 0, 4000, 3000));
        Assert.True(bounds.Right >= 1000 - view.Padding - 1 && bounds.Bottom >= 800 - view.Padding - 1);
    }

    [Fact]
    public void Resizing_the_viewport_refits_only_while_fitted()
    {
        var view = Create();
        view.SetViewport(2000, 1600);
        Assert.Equal(Math.Min((2000 - 48) / 4000d, (1600 - 48) / 3000d), view.Zoom, 3);

        view.ZoomAt(new SKPoint(0, 0), 2);
        view.SetViewport(800, 600);
        Assert.Equal(2, view.Zoom);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(0.00001)]
    public void Zoom_stays_within_limits(double requested)
    {
        var view = Create();

        view.ZoomAt(new SKPoint(10, 10), requested);

        Assert.InRange(view.Zoom, ViewTransform.MinZoom, ViewTransform.MaxZoom);
    }
}
