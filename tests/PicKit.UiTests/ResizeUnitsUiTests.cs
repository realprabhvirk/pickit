using System.IO;
using System.Windows.Media.Imaging;
using PicKit.Core.Imaging;
using PicKit.ViewModels.Tools;

namespace PicKit.UiTests;

public class ResizeUnitsUiTests
{
    private static async Task<ResizeToolViewModel> OpenResize(UiHarness harness)
    {
        await TestFiles.OpenPhoto(harness, 3000, 2000);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Resize;
        harness.Pump();
        return editor.Resize;
    }

    private static UnitChoice Unit(ResizeToolViewModel resize, SizeUnit unit) =>
        resize.Units.First(choice => choice.Unit == unit);

    [Fact]
    public void Pixels_are_the_default_and_show_no_resolution() => UiHarness.Run(async harness =>
    {
        var resize = await OpenResize(harness);

        Assert.Equal(SizeUnit.Pixels, resize.SelectedUnit.Unit);
        Assert.False(resize.ShowsDpi);
        Assert.Equal("Width (pixels)", resize.WidthLabel);
        Assert.Equal("3000", resize.WidthText);
    });

    [Fact]
    public void Switching_units_shows_the_same_size_in_the_new_unit() => UiHarness.Run(async harness =>
    {
        var resize = await OpenResize(harness);

        resize.SelectedUnit = Unit(resize, SizeUnit.Millimetres);
        Assert.True(resize.ShowsDpi);
        Assert.Equal("Width (mm)", resize.WidthLabel);
        Assert.Equal(254, double.Parse(resize.WidthText), 2);
        Assert.Equal(169.33, double.Parse(resize.HeightText), 2);
        Assert.Equal("3000 × 2000", resize.ResultText);
        harness.Screenshot("resize-units");

        resize.SelectedUnit = Unit(resize, SizeUnit.Centimetres);
        Assert.Equal(25.4, double.Parse(resize.WidthText), 2);

        resize.SelectedUnit = Unit(resize, SizeUnit.Pixels);
        Assert.Equal("3000", resize.WidthText);
        Assert.Equal("2000", resize.HeightText);
    });

    [Fact]
    public void Typing_in_mm_works_out_the_pixels_and_keeps_the_shape() => UiHarness.Run(async harness =>
    {
        var resize = await OpenResize(harness);
        resize.SelectedUnit = Unit(resize, SizeUnit.Millimetres);

        resize.WidthText = "100";

        Assert.Equal("1181 × 787", resize.ResultText);
        Assert.Equal(66.63, double.Parse(resize.HeightText), 2);
        Assert.Contains("mm", resize.PrintText);
        Assert.Contains("300 DPI", resize.PrintText);
        Assert.True(resize.IsValid);
    });

    [Fact]
    public void Changing_the_resolution_changes_the_pixels_for_the_same_size() => UiHarness.Run(async harness =>
    {
        var resize = await OpenResize(harness);
        resize.SelectedUnit = Unit(resize, SizeUnit.Centimetres);
        resize.KeepAspect = false;
        resize.WidthText = "10";
        resize.HeightText = "10";
        Assert.Equal("1181 × 1181", resize.ResultText);

        resize.DpiText = "150";

        Assert.Equal("10", resize.WidthText);
        Assert.Equal("591 × 591", resize.ResultText);
    });

    [Fact]
    public void A_bad_resolution_or_size_is_refused() => UiHarness.Run(async harness =>
    {
        var resize = await OpenResize(harness);
        resize.SelectedUnit = Unit(resize, SizeUnit.Millimetres);

        resize.DpiText = "0";
        Assert.False(resize.IsValid);
        Assert.Contains("resolution", resize.Warning);

        resize.DpiText = "300";
        Assert.True(resize.IsValid);

        resize.WidthText = "0.001";
        Assert.False(resize.IsValid);
    });

    [Fact]
    public void Applying_a_print_size_resizes_and_saves_that_resolution_in_the_file() => UiHarness.Run(async harness =>
    {
        var resize = await OpenResize(harness);
        var editor = harness.ViewModel.Editor;
        var document = editor.Document!;
        resize.SelectedUnit = Unit(resize, SizeUnit.Millimetres);
        resize.WidthText = "100";

        await resize.ApplyCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.Equal((1181, 787), (document.Pixels.Width, document.Pixels.Height));
        Assert.Equal(100, double.Parse(resize.WidthText), 1);

        harness.Settings.Current.ConfirmOverwrite = false;
        await harness.ViewModel.SaveCommand.ExecuteAsync(null);
        harness.Pump();

        // Read back by Windows' own decoder, not by anything in PicKit.
        using var file = File.OpenRead(document.Source.Path!);
        var frame = BitmapDecoder.Create(file, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        Assert.Equal(300, frame.DpiX, 0);
        Assert.Equal(300, frame.DpiY, 0);
        Assert.Equal(100, frame.PixelWidth / frame.DpiX * 25.4, 0);
    });

    [Fact]
    public void Resizing_in_pixels_adds_no_resolution() => UiHarness.Run(async harness =>
    {
        var resize = await OpenResize(harness);
        resize.WidthText = "1500";

        await resize.ApplyCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.Null(harness.ViewModel.Editor.Document!.PrintDpi);
    });
}
