using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PicKit.Core.Settings;
using PicKit.Services;
using PicKit.ViewModels;
using PicKit.Views;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PicKit.UiTests;

/// <summary>
/// Runs the real main window on a private UI thread. It is placed off-screen and never
/// activated, and input is delivered through the canvas's own methods, so tests cannot take
/// over the mouse, steal focus, or touch anything else on the desktop.
/// </summary>
internal sealed class UiHarness : IDisposable
{
    private static readonly Lazy<Dispatcher> UiThread = new(StartUiThread);

    private UiHarness(MainWindow window, MainViewModel viewModel, string folder, ScriptedDialogs dialogs, FakeClipboard clipboard, SettingsStore settings)
    {
        Window = window;
        ViewModel = viewModel;
        Folder = folder;
        Dialogs = dialogs;
        Clipboard = clipboard;
        Settings = settings;
    }

    public MainWindow Window { get; }

    public MainViewModel ViewModel { get; }

    public ScriptedDialogs Dialogs { get; }

    public FakeClipboard Clipboard { get; }

    public SettingsStore Settings { get; }

    /// <summary>A private folder for this test's files.</summary>
    public string Folder { get; }

    /// <summary>Runs a test body on the UI thread, with a fresh window.</summary>
    public static void Run(Action<UiHarness> test) => Run(harness =>
    {
        test(harness);
        return Task.CompletedTask;
    });

    public static void Run(Func<UiHarness, Task> test)
    {
        UiThread.Value.Invoke(() =>
        {
            using var harness = Create();
            var task = test(harness);
            harness.WaitFor(() => task.IsCompleted, seconds: 60);
            task.GetAwaiter().GetResult();
        });
    }

    /// <summary>Processes queued UI work, including layout and rendering.</summary>
    public void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    public void WaitFor(Func<bool> condition, double seconds = 15)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("The UI did not reach the expected state in time.");
            }

            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }
    }

    public void Screenshot(string name)
    {
        // Let the render thread present the latest frame before capturing it.
        var settled = DateTime.UtcNow.AddMilliseconds(250);
        WaitFor(() => DateTime.UtcNow > settled);
        Pump();
        var folder = Environment.GetEnvironmentVariable("PICKIT_SHOTS") ?? Path.Combine(Path.GetTempPath(), "PicKitUiShots");
        Directory.CreateDirectory(folder);

        var handle = new WindowInteropHelper(Window).Handle;
        var scale = VisualTreeHelper.GetDpi(Window);
        var width = (int)(Window.ActualWidth * scale.DpiScaleX);
        var height = (int)(Window.ActualHeight * scale.DpiScaleY);

        var memory = NativeShot.CreateCompatible(handle, width, height, out var dc, out var bitmap);
        try
        {
            NativeShot.PrintWindow(handle, dc, NativeShot.RenderFullContent);
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var file = File.Create(Path.Combine(folder, name + ".png"));
            encoder.Save(file);
        }
        finally
        {
            NativeShot.Release(memory, dc, bitmap);
        }
    }

    public void Dispose()
    {
        Dialogs.UnsavedAnswer = UnsavedChoice.Discard;
        Window.Close();
        Pump();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (IOException)
        {
            // Test scratch space; the OS temp cleanup will get it.
        }
    }

    private static UiHarness Create()
    {
        var folder = Path.Combine(Path.GetTempPath(), "PicKitUiTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        var settings = new SettingsStore(Path.Combine(folder, "settings.json"));
        settings.Current.RememberWindow = false;

        var dialogs = new ScriptedDialogs();
        var clipboard = new FakeClipboard();
        var recent = new RecentFiles(Path.Combine(folder, ".recent.json"));
        var viewModel = new MainViewModel(settings, dialogs, clipboard, recent) { RenameHistoryPath = Path.Combine(folder, ".rename-history.json") };
        var window = new MainWindow(viewModel, settings)
        {
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -6000,
            Top = -6000,
            Width = 1000,
            Height = 700,
        };

        window.Show();
        var harness = new UiHarness(window, viewModel, folder, dialogs, clipboard, settings);
        harness.Pump();
        return harness;
    }

    private static Dispatcher StartUiThread()
    {
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            var app = new App(new LaunchInbox());
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        ready.Wait();
        return dispatcher!;
    }

    private static class NativeShot
    {
        public const uint RenderFullContent = 2;

        [DllImport("user32.dll")]
        public static extern bool PrintWindow(IntPtr handle, IntPtr dc, uint flags);

        public static IntPtr CreateCompatible(IntPtr handle, int width, int height, out IntPtr dc, out IntPtr bitmap)
        {
            var screen = GetDC(handle);
            dc = CreateCompatibleDC(screen);
            bitmap = CreateCompatibleBitmap(screen, width, height);
            var previous = SelectObject(dc, bitmap);
            ReleaseDC(handle, screen);
            return previous;
        }

        public static void Release(IntPtr previous, IntPtr dc, IntPtr bitmap)
        {
            SelectObject(dc, previous);
            DeleteObject(bitmap);
            DeleteDC(dc);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr handle, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr handle);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr handle);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);
    }
}
