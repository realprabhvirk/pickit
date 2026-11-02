using PicKit.Core.Imaging;
using SkiaSharp;

namespace PicKit.Tests;

public class ResizeTests
{
    [Fact]
    public void Scale_uses_exactly_the_requested_size_and_never_crops()
    {
        var plan = ResizeCalculator.Plan(1920, 1080, 1280, 720, ResizeMode.Scale);

        Assert.Equal(new ResizePlan(1280, 720, null), plan);
    }

    [Fact]
    public void Fit_matches_the_specification_example()
    {
        var plan = ResizeCalculator.Plan(1920, 1080, 1080, 1080, ResizeMode.Fit);

        Assert.Equal(1080, plan.Width);
        Assert.Equal(608, plan.Height);
        Assert.False(plan.RequiresCrop);
    }

    [Fact]
    public void Fit_does_not_enlarge()
    {
        var plan = ResizeCalculator.Plan(500, 300, 1920, 1080, ResizeMode.Fit);

        Assert.Equal((500, 300), (plan.Width, plan.Height));
    }

    [Fact]
    public void Fit_never_collapses_to_zero()
    {
        var plan = ResizeCalculator.Plan(10_000, 1, 100, 100, ResizeMode.Fit);

        Assert.True(plan.Width >= 1 && plan.Height >= 1);
    }

    [Fact]
    public void Crop_to_fill_declares_the_crop_explicitly()
    {
        var plan = ResizeCalculator.Plan(1920, 1080, 1080, 1080, ResizeMode.CropToFill);

        Assert.True(plan.RequiresCrop);
        Assert.Equal(new SKRectI(420, 0, 1500, 1080), plan.Crop);
        Assert.Equal((1080, 1080), (plan.Width, plan.Height));
    }

    [Fact]
    public void Crop_to_fill_with_matching_shape_needs_no_crop()
    {
        var plan = ResizeCalculator.Plan(3840, 2160, 1920, 1080, ResizeMode.CropToFill);

        Assert.False(plan.RequiresCrop);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-5, 100)]
    public void Invalid_dimensions_are_rejected(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ResizeCalculator.Plan(100, 100, width, height, ResizeMode.Scale));
    }

    [Theory]
    [InlineData(1920, 1080, 1280, 720)]
    [InlineData(1000, 3000, 500, 1500)]
    public void Keeping_proportions_preserves_aspect(int sourceWidth, int sourceHeight, int width, int expectedHeight)
    {
        Assert.Equal(expectedHeight, ResizeCalculator.HeightForWidth(sourceWidth, sourceHeight, width));
        Assert.Equal(width, ResizeCalculator.WidthForHeight(sourceWidth, sourceHeight, expectedHeight));
    }

    [Theory]
    [InlineData(1920, 1080, 1280, 720, false)]
    [InlineData(1920, 1080, 1280, 721, false)]
    [InlineData(1920, 1080, 1080, 1080, true)]
    [InlineData(3, 2, 2, 1, false)]
    public void Stretch_warning_appears_only_for_real_distortion(int sourceWidth, int sourceHeight, int width, int height, bool stretched)
    {
        Assert.Equal(stretched, ResizeCalculator.IsStretched(sourceWidth, sourceHeight, width, height));
    }

    [Fact]
    public void Resize_produces_the_planned_dimensions()
    {
        using var source = TestImages.Photo(800, 600);

        using var result = ImageOps.Resize(source, 321, 123);

        Assert.Equal((321, 123), (result.Width, result.Height));
    }

    [Fact]
    public void Large_reduction_keeps_average_colour()
    {
        using var source = TestImages.Solid(4000, 3000, new SKColor(200, 100, 50));

        using var result = ImageOps.Resize(source, 40, 30);

        var centre = result.Pixels[(15 * 40) + 20];
        Assert.Equal(0xFFC86432, centre);
    }

    [Fact]
    public void Resize_keeps_transparency()
    {
        using var source = TestImages.Solid(100, 100, SKColors.Transparent);

        using var result = ImageOps.Resize(source, 50, 50);

        Assert.True(result.HasTransparency());
        Assert.Equal(0u, result.Pixels[0]);
    }

    [Fact]
    public void Enlarging_works()
    {
        using var source = TestImages.Photo(100, 80);

        using var result = ImageOps.Resize(source, 400, 320);

        Assert.Equal((400, 320), (result.Width, result.Height));
        Assert.False(result.HasTransparency());
    }

    [Fact]
    public void Same_size_returns_an_independent_copy()
    {
        using var source = TestImages.Numbered(10, 10);

        using var result = ImageOps.Resize(source, 10, 10);
        result.Pixels[0] = 0;

        Assert.NotEqual(0u, source.Pixels[0]);
    }

    [Fact]
    public void Plan_with_crop_cuts_then_scales()
    {
        using var source = TestImages.Solid(200, 100, SKColors.Black);
        using (var canvas = source.CreateCanvas())
        using (var paint = new SKPaint { Color = SKColors.White })
        {
            canvas.DrawRect(50, 0, 100, 100, paint);
        }

        var plan = ResizeCalculator.Plan(200, 100, 100, 100, ResizeMode.CropToFill);
        using var result = ImageOps.ApplyPlan(source, plan);

        Assert.Equal((100, 100), (result.Width, result.Height));
        Assert.All(result.Pixels.ToArray(), pixel => Assert.Equal(0xFFFFFFFF, pixel));
    }
}
