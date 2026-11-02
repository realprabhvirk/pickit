using PicKit.Core.Imaging;
using SkiaSharp;

namespace PicKit.Tests;

public class OrthoTransformTests
{
    public static TheoryData<OrthoTransform> All => new(Enum.GetValues<OrthoTransform>());

    [Theory]
    [MemberData(nameof(All))]
    public void Pixels_land_exactly_where_the_matrix_says(OrthoTransform transform)
    {
        using var source = TestImages.Numbered(5, 3);
        using var result = OrthoTransforms.Apply(source, transform);
        var matrix = OrthoTransforms.ToMatrix(transform, source.Width, source.Height);

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                // Pixel centres map to pixel centres under a quarter turn or mirror.
                var mapped = matrix.MapPoint(x + 0.5f, y + 0.5f);
                var targetX = (int)Math.Floor(mapped.X);
                var targetY = (int)Math.Floor(mapped.Y);

                Assert.Equal(source.Pixels[(y * source.Width) + x], result.Pixels[(targetY * result.Width) + targetX]);
            }
        }
    }

    [Theory]
    [InlineData(OrthoTransform.Rotate90, 3, 5)]
    [InlineData(OrthoTransform.Rotate180, 5, 3)]
    [InlineData(OrthoTransform.Rotate270, 3, 5)]
    [InlineData(OrthoTransform.FlipHorizontal, 5, 3)]
    public void Sides_swap_only_for_quarter_turns(OrthoTransform transform, int width, int height)
    {
        using var source = TestImages.Numbered(5, 3);
        using var result = OrthoTransforms.Apply(source, transform);

        Assert.Equal((width, height), (result.Width, result.Height));
    }

    [Fact]
    public void Four_quarter_turns_restore_the_original()
    {
        using var source = TestImages.Numbered(7, 4);
        var current = source.Clone();
        for (var i = 0; i < 4; i++)
        {
            var next = OrthoTransforms.Apply(current, OrthoTransform.Rotate90);
            current.Dispose();
            current = next;
        }

        using (current)
        {
            Assert.True(source.Pixels.SequenceEqual(current.Pixels));
        }
    }

    [Fact]
    public void Rotate90_is_clockwise()
    {
        using var source = TestImages.Solid(4, 2, SKColors.Black);
        source.Pixels[0] = 0xFFFF0000; // top-left

        using var result = OrthoTransforms.Apply(source, OrthoTransform.Rotate90);

        // Clockwise, the top-left corner becomes the top-right corner.
        Assert.Equal(0xFFFF0000, result.Pixels[result.Width - 1]);
    }
}
