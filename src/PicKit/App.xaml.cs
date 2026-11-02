using System.Windows;
using System.Windows.Threading;
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

    private readonly LaunchInbox _inbox;
    private DispatcherTimer? _gather;
    private SettingsStore? _settings;
    private MainViewModel? _viewModel;

    public App(LaunchInbox inbox)
    {
        _inbox = inbox;
        DispatcherUnhandledException += OnUnhandledUiException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ImageDecoder.PlatformDecoder = WicDecoder.TryDecode;
        _settings = new SettingsStore();

        _viewModel = new MainViewModel(_settings, new DialogService(), new ClipboardService());
        var window = new MainWindow(_viewModel, _settings);
        MainWindow = window;
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
        foreach (var request in _inbox.TakeMerged())
        {
            _viewModel?.Handle(request);
        }
    }
}
