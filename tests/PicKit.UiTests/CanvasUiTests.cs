using System.IO;
using System.Windows;
using System.Windows.Input;
using PicKit.Core.Imaging;
using SkiaSharp;

namespace PicKit.UiTests;

public class CanvasUiTests
{
    [Fact]
    public void Window_opens_with_the_empty_state() => UiHarness.Run(harness =>
    {
        Assert.False(harness.ViewModel.HasImage);
        Assert.Equal("PicKit", harness.Window.Title);
        harness.Screenshot("empty");
    });

    [Fact]
    public void Opening_an_image_fits_it_to_the_window() => UiHarness.Run(async harness =>
    {
        await OpenPhoto(harness, 3000, 2000);

        var view = harness.Window.Canvas.View;
        Assert.True(view.IsFitted);
        Assert.True(view.Zoom < 1);
        Assert.Contains("3000 × 2000", harness.ViewModel.StatusText);
        harness.Screenshot("fitted");
    });

    [Fact]
    public void Mouse_wheel_zooms_around_the_cursor() => UiHarness.Run(async harness =>
    {
        await OpenPhoto(harness, 3000, 2000);
        var canvas = harness.Window.Canvas;
        var position = new Point(canvas.ActualWidth * 0.25, canvas.ActualHeight * 0.4);
        var viewPoint = ViewPoint(canvas, position);
        var before = canvas.View.ViewToImage(viewPoint);
        var zoomBefore = canvas.View.Zoom;

        canvas.WheelAt(position, Mouse.MouseWheelDeltaForOneLine * 2);
        harness.Pump();

        Assert.True(canvas.View.Zoom > zoomBefore);
        var after = canvas.View.ViewToImage(viewPoint);
        Assert.Equal(before.X, after.X, 2f);
        Assert.Equal(before.Y, after.Y, 2f);
    });

    [Fact]
    public void Dragging_pans_the_image_once_it_overflows() => UiHarness.Run(async harness =>
    {
        await OpenPhoto(harness, 3000, 2000);
        var canvas = harness.Window.Canvas;
        canvas.ActualSizeCommand.Execute(null);
        harness.Pump();
        var before = canvas.View.ImageToView(new SKPoint(1500, 1000));

        Assert.True(canvas.PressAt(new Point(400, 300), MouseButton.Left, ModifierKeys.None));
        canvas.MoveTo(new Point(350, 260), ModifierKeys.None);
        canvas.ReleaseAt(new Point(350, 260), MouseButton.Left, ModifierKeys.None);
        harness.Pump();

        var after = canvas.View.ImageToView(new SKPoint(1500, 1000));
        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(canvas);
        Assert.Equal(before.X - (50 * scale.DpiScaleX), after.X, 2f);
        Assert.Equal(before.Y - (40 * scale.DpiScaleY), after.Y, 2f);
        harness.Screenshot("panned-100-percent");
    });

    [Fact]
    public void Zoom_commands_step_and_fit_restores() => UiHarness.Run(async harness =>
    {
        await OpenPhoto(harness, 3000, 2000);
        var canvas = harness.Window.Canvas;
        var fitted = canvas.View.Zoom;

        canvas.ZoomInCommand.Execute(null);
        Assert.True(canvas.View.Zoom > fitted);

        canvas.ActualSizeCommand.Execute(null);
        Assert.Equal(1, canvas.View.Zoom);
        Assert.Equal("100%", canvas.ZoomText);

        canvas.FitCommand.Execute(null);
        harness.Pump();
        Assert.Equal(fitted, canvas.View.Zoom, 5);
    });

    [Theory]
    [InlineData(Key.OemPlus)]
    [InlineData(Key.Add)]
    [InlineData(Key.OemMinus)]
    [InlineData(Key.Subtract)]
    [InlineData(Key.D0)]
    [InlineData(Key.NumPad0)]
    public void Zoom_shortcuts_are_bound_to_ctrl(Key key) => UiHarness.Run(harness =>
    {
        var bound = harness.Window.InputBindings.OfType<KeyBinding>()
            .Any(binding => binding.Key == key && binding.Modifiers == ModifierKeys.Control);

        Assert.True(bound);
    });

    private static async Task OpenPhoto(UiHarness harness, int width, int height)
    {
        var path = Path.Combine(harness.Folder, "photo.png");
        using (var image = ImageBuffer.Create(width, height))
        {
            using var canvas = image.CreateCanvas();
            using var paint = new SKPaint();
            paint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(width, height), [SKColors.SteelBlue, SKColors.Orange], SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, width, height, paint);
            using var data = image.Bitmap.PeekPixels().Encode(new SKPngEncoderOptions())!;
            await File.WriteAllBytesAsync(path, data.ToArray());
        }

        harness.ViewModel.OpenPaths([path]);
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);
        harness.Pump();
    }

    private static SKPoint ViewPoint(FrameworkElement element, Point position)
    {
        var scale = System.Windows.Media.VisualTreeHelper.GetDpi(element);
        return new SKPoint((float)(position.X * scale.DpiScaleX), (float)(position.Y * scale.DpiScaleY));
    }
}
