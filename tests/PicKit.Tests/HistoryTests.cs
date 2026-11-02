using System.Collections.Immutable;
using PicKit.Core.Documents;
using PicKit.Core.Imaging;
using PicKit.Core.Markup;
using SkiaSharp;

namespace PicKit.Tests;

public class HistoryTests
{
    private static EditorDocument Document(ImageBuffer pixels, EditHistory? history = null) =>
        new(pixels, new DocumentSource(@"C:\photos\test.png", ImageFormat.Png, 1000), history);

    [Fact]
    public void Undo_restores_every_pixel_and_redo_reapplies()
    {
        using var document = Document(TestImages.Numbered(50, 40));
        using var original = document.Pixels.Clone();

        var cropped = ImageOps.Crop(document.Pixels, new SKRectI(5, 5, 25, 25));
        using var expected = cropped.Clone();
        document.ReplacePixels("Crop", cropped);

        Assert.Equal(20, document.Pixels.Width);

        document.Undo();
        Assert.Equal(50, document.Pixels.Width);
        Assert.True(original.Pixels.SequenceEqual(document.Pixels.Pixels));

        document.Redo();
        Assert.Equal(20, document.Pixels.Width);
        Assert.True(expected.Pixels.SequenceEqual(document.Pixels.Pixels));
    }

    [Fact]
    public void Several_steps_unwind_in_order()
    {
        using var document = Document(TestImages.Numbered(30, 20));
        using var original = document.Pixels.Clone();

        document.ReplacePixels("Rotate", OrthoTransforms.Apply(document.Pixels, OrthoTransform.Rotate90));
        document.ReplacePixels("Resize", ImageOps.Resize(document.Pixels, 10, 15));
        document.ReplacePixels("Flip", OrthoTransforms.Apply(document.Pixels, OrthoTransform.FlipHorizontal));

        Assert.Equal("Flip", document.History.UndoLabel);
        document.Undo();
        document.Undo();
        Assert.Equal((20, 30), (document.Pixels.Width, document.Pixels.Height));
        document.Undo();

        Assert.False(document.History.CanUndo);
        Assert.Equal((30, 20), (document.Pixels.Width, document.Pixels.Height));
        Assert.True(original.Pixels.SequenceEqual(document.Pixels.Pixels));
    }

    [Fact]
    public void Region_patch_undoes_exactly_and_costs_only_the_region()
    {
        using var document = Document(TestImages.Numbered(1000, 1000));
        using var original = document.Pixels.Clone();
        var region = new SKRectI(100, 200, 150, 240);

        document.PatchRegion("Redact", region, ImageOps.SolidRegion(50, 40, SKColors.Black));

        Assert.Equal(0xFF000000, document.Pixels.Pixels[(200 * 1000) + 100]);
        Assert.Equal(50L * 40 * 4, document.History.BytesInMemory);

        document.Undo();
        Assert.True(original.Pixels.SequenceEqual(document.Pixels.Pixels));

        document.Redo();
        Assert.Equal(0xFF000000, document.Pixels.Pixels[(200 * 1000) + 100]);
    }

    [Fact]
    public void A_new_edit_discards_the_redo_branch()
    {
        using var document = Document(TestImages.Numbered(10, 10));
        document.ReplacePixels("One", document.Pixels.Clone());
        document.Undo();
        Assert.True(document.History.CanRedo);

        document.ReplacePixels("Two", document.Pixels.Clone());

        Assert.False(document.History.CanRedo);
    }

    [Fact]
    public void Dirty_tracking_follows_the_saved_state_through_undo_and_redo()
    {
        using var document = Document(TestImages.Numbered(10, 10));
        Assert.False(document.IsDirty);

        document.ReplacePixels("Edit", document.Pixels.Clone());
        Assert.True(document.IsDirty);

        document.MarkSaved(document.Source);
        Assert.False(document.IsDirty);

        document.Undo();
        Assert.True(document.IsDirty);

        document.Redo();
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void Undo_data_moves_to_disk_past_the_budget_and_still_restores()
    {
        // A budget of one image means earlier states must leave memory.
        const int side = 64;
        var budget = (long)side * side * 4;
        using var document = Document(TestImages.Numbered(side, side), new EditHistory(budget));
        using var original = document.Pixels.Clone();

        document.ReplacePixels("A", TestImages.Solid(side, side, SKColors.Red));
        document.ReplacePixels("B", TestImages.Solid(side, side, SKColors.Green));
        document.ReplacePixels("C", TestImages.Solid(side, side, SKColors.Blue));

        Assert.True(document.History.BytesInMemory <= budget);

        document.Undo();
        document.Undo();
        document.Undo();
        Assert.True(original.Pixels.SequenceEqual(document.Pixels.Pixels));
    }

    [Fact]
    public void Steps_beyond_the_limit_drop_the_oldest()
    {
        using var document = Document(TestImages.Solid(4, 4, SKColors.Red));

        for (var i = 0; i < 80; i++)
        {
            document.ReplacePixels($"Step {i}", document.Pixels.Clone());
        }

        var undone = 0;
        while (document.History.CanUndo)
        {
            document.Undo();
            undone++;
        }

        Assert.Equal(64, undone);
    }

    [Fact]
    public void Markup_follows_the_image_through_a_crop_and_undo()
    {
        using var document = Document(TestImages.Solid(100, 100, SKColors.White));
        document.SetMarkups("Add", [new PointMark(new SKPoint(50, 50))]);

        var region = new SKRectI(10, 20, 90, 90);
        document.ReplacePixels(
            "Crop",
            ImageOps.Crop(document.Pixels, region),
            SKMatrix.CreateTranslation(-region.Left, -region.Top));

        Assert.Equal(new SKPoint(40, 30), ((PointMark)document.Markups[0]).At);

        document.Undo();
        Assert.Equal(new SKPoint(50, 50), ((PointMark)document.Markups[0]).At);
    }

    [Fact]
    public void Markup_only_edits_never_copy_pixels()
    {
        using var document = Document(TestImages.Solid(100, 100, SKColors.White));
        var pixels = document.Pixels;

        document.SetMarkups("Add", [new PointMark(new SKPoint(1, 1))]);
        Assert.Same(pixels, document.Pixels);
        Assert.Equal(0, document.History.BytesInMemory);

        document.Undo();
        Assert.Empty(document.Markups);
        Assert.Same(pixels, document.Pixels);
    }

    [Fact]
    public void Flattening_leaves_the_editable_state_alone()
    {
        using var document = Document(TestImages.Solid(10, 10, SKColors.White));
        document.SetMarkups("Add", [new PointMark(new SKPoint(5, 5))]);

        using var flat = document.RenderFlattened();

        Assert.Equal(0xFFFF0000, flat.Pixels[(5 * 10) + 5]);
        Assert.Equal(0xFFFFFFFF, document.Pixels.Pixels[(5 * 10) + 5]);
        Assert.Single(document.Markups);
    }

    private sealed record PointMark(SKPoint At) : MarkupElement
    {
        public override SKRect Bounds => new(At.X, At.Y, At.X + 1, At.Y + 1);

        public override void Draw(SKCanvas canvas)
        {
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(Bounds, paint);
        }

        public override bool HitTest(SKPoint point, float tolerance) => false;

        public override MarkupElement Transformed(SKMatrix matrix) => this with { At = matrix.MapPoint(At) };
    }
}
