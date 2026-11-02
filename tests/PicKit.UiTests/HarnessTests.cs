using System.IO;
using System.Windows;
using PicKit.Core.Diagnostics;

namespace PicKit.UiTests;

/// <summary>The tests share a PC with someone using it, so this has to stay true.</summary>
public class HarnessTests
{
    [Fact]
    public void Only_the_off_screen_window_exists() => UiHarness.Run(harness =>
    {
        var window = Assert.Single(Application.Current.Windows.Cast<Window>());

        Assert.Same(harness.Window, window);
        Assert.True(window.Left + window.ActualWidth < SystemParameters.VirtualScreenLeft);
        Assert.False(window.IsActive);
    });

    [Fact]
    public void Nothing_is_written_to_the_real_app_data()
    {
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PicKit");

        Assert.NotEqual(real, AppPaths.Data, StringComparer.OrdinalIgnoreCase);
        Assert.StartsWith(Path.GetTempPath(), Log.FilePath, StringComparison.OrdinalIgnoreCase);
    }
}
