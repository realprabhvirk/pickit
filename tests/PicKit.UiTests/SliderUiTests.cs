using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PicKit.Controls;

namespace PicKit.UiTests;

public class SliderUiTests
{
    private static Slider OpenTextSizeSlider(UiHarness harness)
    {
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Text;
        harness.Pump();
        return Descendants(harness.Window.ToolPanel).OfType<Slider>().First(slider => slider.IsVisible);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    public void Pressing_anywhere_along_a_slider_goes_to_that_point(double along) => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 800, 600);
        var slider = OpenTextSizeSlider(harness);

        SliderSeeking.SeekTo(slider, new Point(slider.ActualWidth * along, slider.ActualHeight / 2));

        var expected = slider.Minimum + ((slider.Maximum - slider.Minimum) * along);
        Assert.InRange(slider.Value, expected - 12, expected + 12);
    });

    [Fact]
    public void Dragging_keeps_following_the_pointer_and_stops_at_the_ends() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 800, 600);
        var slider = OpenTextSizeSlider(harness);

        var values = new List<double>();
        foreach (var along in new[] { 0.2, 0.4, 0.6, 0.8 })
        {
            SliderSeeking.SeekTo(slider, new Point(slider.ActualWidth * along, 0));
            values.Add(slider.Value);
        }

        Assert.Equal(values.OrderBy(value => value), values);
        Assert.Equal(4, values.Distinct().Count());

        SliderSeeking.SeekTo(slider, new Point(slider.ActualWidth + 500, 0));
        Assert.Equal(slider.Maximum, slider.Value);

        SliderSeeking.SeekTo(slider, new Point(-500, 0));
        Assert.Equal(slider.Minimum, slider.Value);
    });

    [Fact]
    public void Seeking_lands_on_whole_steps_when_the_slider_has_them() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 800, 600);
        var slider = OpenTextSizeSlider(harness);

        SliderSeeking.SeekTo(slider, new Point(slider.ActualWidth * 0.37, 0));

        Assert.Equal(Math.Round(slider.Value), slider.Value);
    });

    [Fact]
    public void A_press_on_the_track_is_taken_by_the_slider_not_its_page_buttons() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 800, 600);
        var slider = OpenTextSizeSlider(harness);
        var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };

        slider.RaiseEvent(press);

        Assert.True(press.Handled);
        slider.ReleaseMouseCapture();
    });
}
