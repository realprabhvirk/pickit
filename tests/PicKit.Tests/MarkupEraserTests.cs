using System.Collections.Immutable;
using PicKit.Core.Markup;
using SkiaSharp;

namespace PicKit.Tests;

public class MarkupEraserTests
{
    private static readonly MarkupStyle Style = new(SKColors.Red, 4);

    private static StrokeElement Line(float y = 100, bool isHighlighter = false) => new()
    {
        Style = Style,
        Points = [new SKPoint(0, y), new SKPoint(100, y), new SKPoint(200, y)],
        IsHighlighter = isHighlighter,
    };

    private static ImmutableArray<MarkupElement> Erase(MarkupElement element, SKPoint from, SKPoint to, float radius = 10) =>
        MarkupEraser.Erase([element], from, to, radius);

    [Fact]
    public void Missing_everything_returns_the_same_list()
    {
        ImmutableArray<MarkupElement> elements = [Line()];

        var result = MarkupEraser.Erase(elements, new SKPoint(100, 300), new SKPoint(200, 300), 10);

        Assert.True(result == elements);
    }

    [Fact]
    public void A_cut_through_the_middle_leaves_two_strokes_either_side()
    {
        var result = Erase(Line(), new SKPoint(100, 100), new SKPoint(100, 100));

        var left = Assert.IsType<StrokeElement>(result[0]);
        var right = Assert.IsType<StrokeElement>(result[1]);
        Assert.Equal(2, result.Length);
        Assert.True(left.Points.Max(point => point.X) < 90);
        Assert.True(right.Points.Min(point => point.X) > 110);
    }

    [Fact]
    public void The_pieces_are_separate_elements_with_the_same_look()
    {
        var stroke = Line(isHighlighter: true);

        var result = Erase(stroke, new SKPoint(100, 100), new SKPoint(100, 100));

        var pieces = result.Cast<StrokeElement>().ToList();
        Assert.All(pieces, piece => Assert.True(piece.IsHighlighter));
        Assert.All(pieces, piece => Assert.Equal(Style, piece.Style));
        Assert.NotEqual(pieces[0].Id, pieces[1].Id);
    }

    [Fact]
    public void An_eraser_cannot_fall_through_a_gap_between_widely_spaced_points()
    {
        var fast = new StrokeElement { Style = Style, Points = [new SKPoint(0, 100), new SKPoint(1000, 100)] };

        var result = Erase(fast, new SKPoint(500, 100), new SKPoint(500, 100));

        Assert.Equal(2, result.Length);
    }

    [Fact]
    public void A_fast_drag_is_followed_all_the_way_between_two_pointer_samples()
    {
        var result = Erase(Line(), new SKPoint(100, 0), new SKPoint(100, 300));

        Assert.Equal(2, result.Length);
    }

    [Fact]
    public void The_visible_edge_of_a_thick_stroke_counts_not_just_its_centre()
    {
        var thick = new StrokeElement { Style = new MarkupStyle(SKColors.Red, 40), Points = [new SKPoint(0, 100), new SKPoint(200, 100)] };

        // The eraser only reaches the stroke's edge, 20 pixels from its centre.
        var result = Erase(thick, new SKPoint(100, 125), new SKPoint(100, 125));

        Assert.Equal(2, result.Length);
    }

    [Fact]
    public void Rubbing_along_a_whole_stroke_removes_it()
    {
        var result = Erase(Line(), new SKPoint(0, 100), new SKPoint(200, 100));

        Assert.Empty(result);
    }

    [Fact]
    public void No_stray_dots_are_left_at_a_cut()
    {
        var result = Erase(Line(), new SKPoint(5, 100), new SKPoint(5, 100));

        Assert.All(result.Cast<StrokeElement>(), piece => Assert.True(piece.Points.Length >= 2));
    }

    [Fact]
    public void Shapes_lines_and_text_go_whole_when_touched()
    {
        MarkupElement shape = new ShapeElement { Style = Style, Kind = ShapeKind.Rectangle, Box = new SKRect(50, 50, 150, 150) };
        MarkupElement line = new LineElement { Style = Style, Start = new SKPoint(0, 100), End = new SKPoint(200, 100) };
        MarkupElement text = new TextElement { Style = Style, Position = new SKPoint(100, 100), Text = "Hello", FontSize = 30 };

        Assert.Empty(Erase(shape, new SKPoint(50, 100), new SKPoint(50, 100)));
        Assert.Empty(Erase(line, new SKPoint(100, 100), new SKPoint(100, 100)));
        Assert.Empty(Erase(text, new SKPoint(110, 110), new SKPoint(110, 110)));
    }

    [Fact]
    public void The_inside_of_an_unfilled_shape_is_empty_space()
    {
        MarkupElement shape = new ShapeElement { Style = Style, Kind = ShapeKind.Rectangle, Box = new SKRect(0, 0, 400, 400) };

        Assert.Single(Erase(shape, new SKPoint(200, 200), new SKPoint(200, 200)));
    }

    [Fact]
    public void Only_what_was_touched_changes()
    {
        var kept = Line(y: 500);
        ImmutableArray<MarkupElement> elements = [Line(), kept];

        var result = MarkupEraser.Erase(elements, new SKPoint(100, 100), new SKPoint(100, 100), 10);

        Assert.Same(kept, result[^1]);
    }

    [Fact]
    public void Stacking_order_is_kept()
    {
        var bottom = Line(y: 300);
        var top = Line(y: 500);
        ImmutableArray<MarkupElement> elements = [bottom, Line(), top];

        var result = MarkupEraser.Erase(elements, new SKPoint(100, 100), new SKPoint(100, 100), 10);

        Assert.Same(bottom, result[0]);
        Assert.Same(top, result[^1]);
    }
}
