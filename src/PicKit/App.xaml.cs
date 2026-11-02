using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using PicKit.Controls;
using PicKit.Core.Diagnostics;
using PicKit.Core.Files;
using PicKit.Core.Imaging;
using PicKit.Core.Settings;
using PicKit.Services;
using PicKit.ViewModels;
using PicKit.Views;

namespace PicKit;

public partial class App : Application
{
    /// <summary>Long enough for Explorer to finish starting one process per selected file.</summary>
    private static readonly TimeSpan GatherDelay = TimeSpan.FromMilliseconds(350);

    private static bool _panelsLoaded;

    private readonly LaunchInbox _inbox;
    private DispatcherTimer? _gather;
    private SettingsStore? _settings;
    private MainViewModel? _viewModel;
    private bool _hasHandledFirstRequest;

    /// <summary>WPF runs startup by itself, so the UI tests need a way to keep this window closed.</summary>
    public bool OpensMainWindow { get; init; } = true;

    public App(LaunchInbox inbox)
    {
        _inbox = inbox;
        DispatcherUnhandledException += OnUnhandledUiException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        SliderSeeking.Register();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ImageDecoder.PlatformDecoder = WicDecoder.TryDecode;
        if (!OpensMainWindow)
        {
            return;
        }

        _settings = new SettingsStore();
        if (_settings.Current.Appearance != AppAppearance.System)
        {
            AppearanceService.Apply(_settings.Current.Appearance);
        }

        _viewModel = new MainViewModel(_settings, new DialogService(), new ClipboardService());
        var window = new MainWindow(_viewModel, _settings);
        MainWindow = window;
        window.ContentRendered += (_, _) => Log.Info($"First paint after {(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds:0} ms");
        window.Show();

        _gather = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher) { Interval = GatherDelay };
        _gather.Tick += OnGatherFinished;
        _inbox.RequestArrived += OnRequestArrived;
        OnRequestArrived();

        // Sweeping old temp folders can touch the disk, so it never delays the first paint.
        _ = Task.Run(TempFiles.SweepAbandonedSessions);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _settings?.Save();
        TempFiles.DeleteSessionFolder();
        base.OnExit(e);
    }

    /// <summary>Tool panels aren't needed until there's an image, so they don't slow the first paint.</summary>
    public static void EnsurePanelsLoaded()
    {
        if (_panelsLoaded || Current is null)
        {
            return;
        }

        _panelsLoaded = true;
        var dictionaries = Current.Resources.MergedDictionaries;
        dictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PicKit;component/Views/ToolPanels.xaml") });
        dictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/PicKit;component/Views/Pages.xaml") });
    }

    private static void OnUnhandledUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);
        e.Handled = true;

        new DialogService().ShowError(
            "Something went wrong.",
            "PicKit ran into a problem and couldn't finish that action. Your open image hasn't been changed.",
            e.Exception.ToString());
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        Log.Error("Unhandled exception", e.ExceptionObject as Exception);

    private void OnRequestArrived()
    {
        // Requests arrive on the listener thread, and each one restarts the wait, so that a
        // whole selection is handled as one job.
        Dispatcher.BeginInvoke(() =>
        {
            _gather?.Stop();
            _gather?.Start();
        });
    }

    private void OnGatherFinished(object? sender, EventArgs e)
    {
        _gather?.Stop();

        // The first request is this launch itself, and Windows has already placed the window.
        // Pulling it forward would undo a shortcut set to start minimized.
        var bringToFront = _hasHandledFirstRequest;
        _hasHandledFirstRequest = true;

        foreach (var request in _inbox.TakeMerged())
        {
            _viewModel?.Handle(request, bringToFront);
        }
    }
}
