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
    private readonly string[] _arguments;
    private SettingsStore? _settings;

    public App(string[] arguments)
    {
        _arguments = arguments;
        DispatcherUnhandledException += OnUnhandledUiException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ImageDecoder.PlatformDecoder = WicDecoder.TryDecode;
        _settings = new SettingsStore();

        var viewModel = new MainViewModel(_settings, new DialogService(), new ClipboardService());
        var window = new MainWindow(viewModel, _settings);
        MainWindow = window;
        window.Show();

        viewModel.OpenPaths(_arguments);

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
}
