using System.Diagnostics;
using PicKit.Core.Background;
using PicKit.Core.Shell;

namespace PicKit.Services;

/// <summary>Starts and stops the background helper from the app. Replaced in tests.</summary>
public class BackgroundControl
{
    private const int StopWaitMilliseconds = 2000;
    private const int PollMilliseconds = 50;

    public virtual void Start()
    {
        if (!BackgroundSignals.IsRunning)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!, LaunchRequest.SwitchFor(ShellAction.Background))
            {
                UseShellExecute = false,
            };

            using var process = Process.Start(start);
        }
    }

    public virtual void Stop() => BackgroundSignals.Stop();

    /// <summary>Picks up a change to what the helper shows, which it reads as it starts.</summary>
    public virtual void Restart()
    {
        Stop();
        var waited = 0;
        while (BackgroundSignals.IsRunning && waited < StopWaitMilliseconds)
        {
            Thread.Sleep(PollMilliseconds);
            waited += PollMilliseconds;
        }

        Start();
    }
}
