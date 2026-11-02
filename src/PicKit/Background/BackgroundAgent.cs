using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using PicKit.Core.Background;
using PicKit.Core.Diagnostics;
using PicKit.Core.Imaging;
using PicKit.Core.Shell;
using SkiaSharp;

namespace PicKit.Background;

/// <summary>
/// The small part of PicKit that's always running. It holds the snip hotkey and the ribbon, and
/// does nothing else until one of them is used. The full app only starts when there's something
/// to show, and closing it gives all of its memory back.
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
    private readonly KeyChordWatcher _keys = new();
    private readonly TriggerGate _gate = new();
    private readonly string _executable;
    private readonly SnipOverlay _overlay;
    private readonly EdgeRibbonWindow? _ribbon;

    private BackgroundAgent(string executable)
        : base(Native.WS_POPUP, Native.WS_EX_TOOLWINDOW)
    {
        _executable = executable;
        _overlay = new SnipOverlay(OnSnipped, TrimMemorySoon);

        if (_settings.Load().ShowRibbon)
        {
            _ribbon = new EdgeRibbonWindow(_settings, () => Launch([]), () => StartSnip("the ribbon"), OnDropped);
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
        ShellRegistration.ForCurrentUser(executable).RegisterNotifications();

        using var stop = new EventWaitHandle(false, EventResetMode.AutoReset, BackgroundSignals.StopEventName);
        using var capture = new EventWaitHandle(false, EventResetMode.AutoReset, BackgroundSignals.CaptureEventName);
        using var agent = new BackgroundAgent(executable);

        agent.RegisterHotkey();
        agent.ListenToKeyboard();
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

    /// <summary>
    /// Puts a screenshot in the inbox as a PNG, and returns it ready for the clipboard too.
    /// </summary>
    internal static (string Path, IReadOnlyList<ClipboardItem> Items) Store(ImageInbox inbox, ImageBuffer image, DateTime time)
    {
        var items = ClipboardCodec.Encode(image, CopyMode.Standard, SKColors.White);
        var png = items.First(item => item.Format == ClipboardCodec.PngFormat).Data;
        return (inbox.Add(png, ImageInbox.ScreenshotName(time)), items);
    }

    public override void Dispose()
    {
        _ribbon?.Dispose();
        _overlay.Dispose();
        base.Dispose();
    }

    protected override IntPtr OnMessage(uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case Native.WM_HOTKEY:
                StartSnip("the hotkey");
                return 0;
            case Native.WM_INPUT:
                OnRawInput(lParam);
                return base.OnMessage(message, wParam, lParam);
            case RunQueued:
                while (_queue.TryDequeue(out var action))
                {
                    action();
                }

                return 0;
            case Native.WM_DISPLAYCHANGE or Native.WM_SETTINGCHANGE:
                _ribbon?.PlaceOnScreen();
                return base.OnMessage(message, wParam, lParam);
            default:
                return base.OnMessage(message, wParam, lParam);
        }
    }

    private static void Notify(string title, string detail) => WindowsToast.Show(ToastContent.Build(title, detail));

    private static void ShowNoImage() => Notify("That isn't an image", "PicKit couldn't find a picture in what you dropped.");

    private void RegisterHotkey()
    {
        // Reserving it stops the keys also reaching the app in use. Raw input still sees them
        // if another app got there first, so this failing isn't worth interrupting anyone for.
        if (!Native.RegisterHotKey(Handle, HotkeyId, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, HotkeyLetter))
        {
            Log.Warn("Ctrl+Alt+S is already taken by another app");
        }
    }

    /// <summary>
    /// Reads the keyboard directly as well as through the hotkey, because another app's keyboard
    /// hook can swallow a real key press before Windows' hotkey system ever sees it.
    /// </summary>
    private void ListenToKeyboard()
    {
        const ushort genericDesktop = 1;
        const ushort keyboard = 6;
        var device = new Native.RAWINPUTDEVICE
        {
            usUsagePage = genericDesktop,
            usUsage = keyboard,
            dwFlags = Native.RIDEV_INPUTSINK,
            hwndTarget = Handle,
        };

        if (!Native.RegisterRawInputDevices([device], 1, Marshal.SizeOf<Native.RAWINPUTDEVICE>()))
        {
            Log.Warn($"Could not read the keyboard directly: error {Marshal.GetLastWin32Error()}");
        }
    }

    private unsafe void OnRawInput(IntPtr input)
    {
        const uint bufferSize = 64;
        var buffer = stackalloc byte[(int)bufferSize];
        var size = bufferSize;
        var headerSize = (uint)Marshal.SizeOf<Native.RAWINPUTHEADER>();
        if (Native.GetRawInputData(input, Native.RID_INPUT, buffer, ref size, headerSize) == uint.MaxValue)
        {
            return;
        }

        if (((Native.RAWINPUTHEADER*)buffer)->dwType != Native.RIM_TYPEKEYBOARD)
        {
            return;
        }

        var key = (Native.RAWKEYBOARD*)(buffer + headerSize);
        var isDown = (key->Flags & Native.RI_KEY_BREAK) == 0;
        if (_keys.OnKey(key->VKey, isDown, Environment.TickCount64))
        {
            StartSnip("the keyboard");
        }
    }

    private void WatchSignals(EventWaitHandle stop, EventWaitHandle capture)
    {
        var thread = new Thread(() =>
        {
            WaitHandle[] handles = [stop, capture];
            while (WaitHandle.WaitAny(handles) != 0)
            {
                Post(() => StartSnip("a request"));
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

    /// <summary>The whole screen is read at once, before anything on it can change.</summary>
    private void StartSnip(string source)
    {
        if (_overlay.IsOpen || !_gate.TryPass(Environment.TickCount64))
        {
            return;
        }

        Log.Info($"Snip started from {source}");
        try
        {
            _overlay.Begin(ScreenCapture.Freeze());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ImageException)
        {
            Log.Warn("Could not capture the screen", ex);
            Notify("Couldn't take that screenshot", "The screen wasn't available.");
        }
    }

    private void OnSnipped(ImageBuffer image)
    {
        var time = DateTime.Now;
        _ = Task.Run(() => StoreScreenshot(image, time));
    }

    private void StoreScreenshot(ImageBuffer image, DateTime time)
    {
        using (image)
        {
            try
            {
                var (path, items) = Store(_inbox, image, time);
                Post(() => FinishScreenshot(path, items));
            }
            catch (Exception ex)
            {
                // This runs on a worker thread, where anything unhandled would vanish in silence.
                Log.Warn("Could not store a screenshot", ex);
                Post(() => Notify("Couldn't save that screenshot", "Check there's space on this PC."));
            }
        }
    }

    private void FinishScreenshot(string path, IReadOnlyList<ClipboardItem> items)
    {
        var isOnClipboard = ClipboardWriter.Set(Handle, items);
        var detail = isOnClipboard
            ? "It's on your clipboard. Click to save and open it."
            : "Click to save and open it.";

        WindowsToast.Show(ToastContent.Build("Screenshot taken", detail, path, ScreenshotLink.For(Path.GetFileName(path))));
        TrimMemorySoon();
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
