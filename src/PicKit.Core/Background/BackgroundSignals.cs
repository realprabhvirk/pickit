namespace PicKit.Core.Background;

/// <summary>How other processes reach the background helper, which has no window to talk to.</summary>
public static class BackgroundSignals
{
    public const string MutexName = @"Local\PicKit.Background";
    public const string StopEventName = @"Local\PicKit.Background.Stop";
    public const string CaptureEventName = @"Local\PicKit.Background.Capture";

    public static bool IsRunning
    {
        get
        {
            if (Mutex.TryOpenExisting(MutexName, out var mutex))
            {
                mutex.Dispose();
                return true;
            }

            return false;
        }
    }

    public static void Stop() => Signal(StopEventName);

    public static void TakeScreenshot() => Signal(CaptureEventName);

    private static void Signal(string name)
    {
        if (EventWaitHandle.TryOpenExisting(name, out var handle))
        {
            handle.Set();
            handle.Dispose();
        }
    }
}
