using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using PicKit.Background;
using PicKit.Core.Background;
using PicKit.Core.Diagnostics;
using PicKit.Core.Shell;

namespace PicKit;

public static class Program
{
    private const string InstanceName = "PicKit.Instance";

    [STAThread]
    public static int Main(string[] args)
    {
        var request = LaunchRequest.Parse(args);
        if (RunInstallerTask(request.Action))
        {
            return 0;
        }

        switch (request.Action)
        {
            case ShellAction.Background:
                return BackgroundAgent.Run(Environment.ProcessPath!);
            case ShellAction.StopBackground:
                BackgroundSignals.Stop();
                return 0;
            case ShellAction.TakeScreenshot:
                BackgroundSignals.TakeScreenshot();
                return 0;
        }

        using var channel = new InstanceChannel(InstanceName);
        if (!channel.IsPrimary && HandOff(channel, request))
        {
            return 0;
        }

        // Started before WPF, so files sent during startup aren't turned away.
        var inbox = new LaunchInbox();
        if (channel.IsPrimary)
        {
            channel.Listen(inbox.Add);
        }

        inbox.Add(request);
        return RunApp(inbox);
    }

    /// <summary>Kept apart from Main so the background helper never loads WPF.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunApp(LaunchInbox inbox)
    {
        var app = new App(inbox);
        app.InitializeComponent();
        return app.Run();
    }

    /// <summary>Lets the installer change Explorer integration without showing any UI.</summary>
    private static bool RunInstallerTask(ShellAction action)
    {
        if (action is not (ShellAction.RegisterMenu or ShellAction.RegisterOpenWith or ShellAction.RegisterBackground or ShellAction.Unregister))
        {
            return false;
        }

        var executable = Environment.ProcessPath!;
        var registration = ShellRegistration.ForCurrentUser(executable);
        switch (action)
        {
            case ShellAction.RegisterMenu:
                registration.RegisterMenu();
                break;
            case ShellAction.RegisterOpenWith:
                registration.RegisterOpenWith();
                break;
            case ShellAction.RegisterBackground:
                registration.RegisterBackground();
                return true;
            default:
                registration.UnregisterAll();
                break;
        }

        ShellNotifications.AssociationsChanged();
        return true;
    }

    private static bool HandOff(InstanceChannel channel, LaunchRequest request)
    {
        // Windows only lets the foreground app bring another to the front, so pass that on.
        AllowSetForegroundWindow(AnyProcess);
        var delivered = channel.Send(request);
        if (!delivered)
        {
            Log.Warn("Falling back to a separate window because the running PicKit didn't respond");
        }

        return delivered;
    }

    private const int AnyProcess = -1;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
