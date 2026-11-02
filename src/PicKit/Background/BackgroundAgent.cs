using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using PicKit.Core.Background;
using PicKit.Core.Diagnostics;
using PicKit.Core.Imaging;
using PicKit.Core.Shell;

namespace PicKit.Background;

/// <summary>
/// The small part of PicKit that's always running. It holds the screenshot hotkey and the drop
/// box, and does nothing else until one of them is used. The full app only starts when there's
/// something to show, and closing it gives all of its memory back.
/// </summary>
internal sealed class BackgroundAgent : AgentWindow
{
    private const int HotkeyId = 1;
    private const uint HotkeyLetter = 'S';
    private const uint RunQueued = Native.WM_APP + 1;
    private const int MaxPathsPerLaunch = 200;

    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(4);

    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly ImageInbox _inbox = new();
    private readonly BackgroundSettingsStore _settings = new();
    private readonly string _executable;
    private readonly NotificationWindow _notification;
    private readonly DropBoxWindow? _dropBox;

    private BackgroundAgent(string executable)
        : base(Native.WS_POPUP, Native.WS_EX_TOOLWINDOW)
    {
        _executable = executable;
        _notification = new NotificationWindow(TrimMemorySoon);

        if (_settings.Load().ShowDropBox)
        {
            _dropBox = new DropBoxWindow(_settings, () => Launch([]), OnDropped);
        }
    }

    /// <summary>Runs until signed out or told to stop. Only one ever runs at a time.</summary>
    public static int Run(string executable)
    {
        using var instance = new Mutex(initiallyOwned: true, BackgroundSignals.MutexName, out var isFirst);
        if (!isFirst)
        {
            return 0;
        }

        Native.OleInitialize(0);
        using var stop = new EventWaitHandle(false, EventResetMode.AutoReset, BackgroundSignals.StopEventName);
        using var capture = new EventWaitHandle(false, EventResetMode.AutoReset, BackgroundSignals.CaptureEventName);
        using var agent = new BackgroundAgent(executable);

        agent.RegisterHotkey();
        agent.WatchSignals(stop, capture);
        agent.TrimMemorySoon();

        while (Native.GetMessageW(out var message, 0, 0, 0) > 0)
        {
            Native.TranslateMessage(ref message);
            Native.DispatchMessageW(ref message);
        }

        Native.UnregisterHotKey(agent.Handle, HotkeyId);
        return 0;
    }

    public override void Dispose()
    {
        _dropBox?.Dispose();
        _notification.Dispose();
        base.Dispose();
    }

    protected override IntPtr OnMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case Native.WM_HOTKEY:
                TakeScreenshot();
                return 0;
            case RunQueued:
                while (_queue.TryDequeue(out var action))
                {
                    action();
                }

                return 0;
            case Native.WM_DISPLAYCHANGE:
                _dropBox?.PlaceOnScreen();
                return 0;
            case Native.WM_CLOSE:
                Native.PostQuitMessage(0);
                return 0;
            default:
                return base.OnMessage(message, wParam, lParam);
        }
    }

    private void RegisterHotkey()
    {
        if (!Native.RegisterHotKey(Handle, HotkeyId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, HotkeyLetter))
        {
            Log.Warn("Ctrl+Alt+S is already taken by another app");
            _notification.Show("Ctrl+Alt+S isn't available", "Another app is using this shortcut,\nso PicKit can't take screenshots.", null, null);
        }
    }

    private void WatchSignals(EventWaitHandle stop, EventWaitHandle capture)
    {
        var thread = new Thread(() =>
        {
            WaitHandle[] handles = [stop, capture];
            while (WaitHandle.WaitAny(handles) != 0)
            {
                Post(TakeScreenshot);
            }

            Native.PostMessageW(Handle, Native.WM_CLOSE, 0, 0);
        })
        {
            IsBackground = true,
            Name = "PicKit background signals",
        };

        thread.Start();
    }

    private void Post(Action action)
    {
        _queue.Enqueue(action);
        Native.PostMessageW(Handle, RunQueued, 0, 0);
    }

    /// <summary>The pixels are read at once, before anything on screen can change.</summary>
    private void TakeScreenshot()
    {
        ImageBuffer image;
        var time = DateTime.Now;
        try
        {
            image = ScreenCapture.MonitorUnderCursor();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ImageException)
        {
            Log.Warn("Could not capture the screen", ex);
            _notification.Show("Couldn't take that screenshot", "The screen wasn't available.", null, null);
            return;
        }

        var thumbnailHeight = NotificationWindow.ThumbnailHeight;
        _ = Task.Run(() => StoreScreenshot(image, time, thumbnailHeight));
    }

    /// <summary>Puts a screenshot in the inbox as a PNG. The caller owns the returned thumbnail.</summary>
    internal static (string Path, ImageBuffer Thumbnail) Store(ImageInbox inbox, ImageBuffer image, DateTime time, int thumbnailHeight)
    {
        var data = ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png));
        var path = inbox.Add(data, ImageInbox.ScreenshotName(time));
        var thumbnailWidth = Math.Max(1, image.Width * thumbnailHeight / image.Height);
        return (path, ImageOps.Resize(image, thumbnailWidth, thumbnailHeight));
    }

    private void StoreScreenshot(ImageBuffer image, DateTime time, int thumbnailHeight)
    {
        using (image)
        {
            try
            {
                var (path, thumbnail) = Store(_inbox, image, time, thumbnailHeight);
                Post(() => ShowScreenshot(path, thumbnail));
            }
            catch (Exception ex) when (ex is ImageException or IOException or UnauthorizedAccessException)
            {
                Log.Warn("Could not store a screenshot", ex);
                Post(() => _notification.Show("Couldn't save that screenshot", "Check there's space on this PC.", null, null));
            }
        }
    }

    private void ShowScreenshot(string path, ImageBuffer thumbnail)
    {
        using (thumbnail)
        {
            _notification.Show("Screenshot taken", "Click to save and open.\nSwipe away to decide later.", thumbnail, () => KeepAndOpen(path));
        }
    }

    private void KeepAndOpen(string path)
    {
        // It may already have been dealt with in the app, while this was on screen.
        if (!File.Exists(path))
        {
            Launch([]);
            return;
        }

        try
        {
            Launch([_inbox.Keep(path, _settings.SaveFolder)]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not save a screenshot", ex);
            Launch([]);
        }
    }

    private void OnDropped(DroppedItems items)
    {
        if (items.Paths.Count > 0)
        {
            Launch(items.Paths.Take(MaxPathsPerLaunch).ToList());
        }
        else if (items.File is { } file)
        {
            OpenDropped(file);
        }
        else if (items.Url is { } address)
        {
            _ = Task.Run(async () =>
            {
                var fetched = await ImageFetcher.FetchAsync(address);
                Post(() => OpenDropped(fetched));
            });
        }
        else
        {
            ShowNoImage();
        }
    }

    private void OpenDropped(DroppedFile? file)
    {
        var path = file is null ? null : _inbox.AddDropped(file);
        if (path is null)
        {
            ShowNoImage();
        }
        else
        {
            Launch([path]);
        }
    }

    private void ShowNoImage() => _notification.Show("That isn't an image", "PicKit couldn't find a picture in what you dropped.", null, null);

    private void Launch(IReadOnlyList<string> paths)
    {
        var start = new ProcessStartInfo(_executable) { UseShellExecute = false };
        if (paths.Count > 0)
        {
            start.ArgumentList.Add(LaunchRequest.SwitchFor(ShellAction.Open));
            start.ArgumentList.Add("--");
            foreach (var path in paths)
            {
                start.ArgumentList.Add(path);
            }
        }

        // Clicking here is what the user did, so the window it opens may come to the front.
        Native.AllowSetForegroundWindow(Native.ASFW_ANY);
        using var process = Process.Start(start);
        TrimMemorySoon();
    }

    /// <summary>Hands back what the last job used, a moment after the work has settled.</summary>
    private void TrimMemorySoon()
    {
        _ = Task.Delay(SettleTime).ContinueWith(_ =>
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Native.SetProcessWorkingSetSize(Native.GetCurrentProcess(), -1, -1);
        }, TaskScheduler.Default);
    }
}
