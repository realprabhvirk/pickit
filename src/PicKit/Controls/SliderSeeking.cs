using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace PicKit.Controls;

/// <summary>
/// Lets every slider be pressed and dragged anywhere along its length. WPF's own track only
/// jumps a page at a time, and dragging works from the thumb alone.
/// </summary>
public static class SliderSeeking
{
    public static void Register()
    {
        var type = typeof(Slider);
        EventManager.RegisterClassHandler(type, UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnPress));
        EventManager.RegisterClassHandler(type, UIElement.PreviewMouseMoveEvent, new MouseEventHandler(OnMove));
        EventManager.RegisterClassHandler(type, UIElement.PreviewMouseLeftButtonUpEvent, new MouseButtonEventHandler(OnRelease));
    }

    /// <summary>Moves the slider to a point in its own coordinates, honouring its tick snapping.</summary>
    public static void SeekTo(Slider slider, Point position)
    {
        if (TrackOf(slider) is not { } track)
        {
            return;
        }

        var value = track.ValueFromPoint(slider.TranslatePoint(position, track));
        if (slider.IsSnapToTickEnabled && slider.TickFrequency > 0)
        {
            var ticks = Math.Round((value - slider.Minimum) / slider.TickFrequency);
            value = slider.Minimum + (ticks * slider.TickFrequency);
        }

        slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
    }

    private static void OnPress(object sender, MouseButtonEventArgs e)
    {
        // The thumb has its own drag, which keeps it from jumping under the pointer.
        var slider = (Slider)sender;
        if (TrackOf(slider) is not { Thumb.IsMouseOver: false })
        {
            return;
        }

        slider.Focus();
        SeekTo(slider, e.GetPosition(slider));
        slider.CaptureMouse();
        e.Handled = true;
    }

    private static void OnMove(object sender, MouseEventArgs e)
    {
        var slider = (Slider)sender;
        if (slider.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            SeekTo(slider, e.GetPosition(slider));
            e.Handled = true;
        }
    }

    private static void OnRelease(object sender, MouseButtonEventArgs e)
    {
        var slider = (Slider)sender;
        if (slider.IsMouseCaptured)
        {
            slider.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private static Track? TrackOf(Slider slider) => slider.Template?.FindName("PART_Track", slider) as Track;
}
