using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using PicKit.Core.Diagnostics;

namespace PicKit.Core.Shell;

/// <summary>
/// Passes launch requests to the PicKit that's already running. Explorer starts one process per
/// selected file, so without this, selecting 50 images would open 50 windows. Instead, each
/// extra process hands over its file and exits before any UI has started.
/// </summary>
public sealed class InstanceChannel : IDisposable
{
    private const int ConnectTimeoutMs = 5000;
    private const int WakeTimeoutMs = 500;

    private readonly string _pipeName;
    private readonly Mutex _mutex;
    private Thread? _listener;
    private volatile bool _isStopping;

    public InstanceChannel(string name)
    {
        _pipeName = $"{name}.{Environment.UserName}";
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{name}", out var createdNew);
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    /// <summary>
    /// Starts listening, before the UI exists. This gets a thread of its own, because a burst of
    /// launches must never have to wait for the thread pool.
    /// </summary>
    public void Listen(Action<LaunchRequest> onRequest)
    {
        _listener = new Thread(() => Serve(onRequest)) { IsBackground = true, Name = "PicKit launch listener" };
        _listener.Start();
    }

    /// <summary>Sends a request to the running instance. Returns false if it couldn't be reached.</summary>
    public bool Send(LaunchRequest request)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(ConnectTimeoutMs);
            JsonSerializer.Serialize(pipe, request, ChannelJsonContext.Default.LaunchRequest);
            pipe.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            Log.Warn("Could not reach the running instance", ex);
            return false;
        }
    }

    public void Dispose()
    {
        StopListening();

        try
        {
            if (IsPrimary)
            {
                _mutex.ReleaseMutex();
            }
        }
        catch (ApplicationException)
        {
            // Released from another thread at shutdown. Windows frees it with the process.
        }

        _mutex.Dispose();
    }

    private void StopListening()
    {
        if (_listener is null)
        {
            return;
        }

        _isStopping = true;
        try
        {
            // The listener is blocked waiting for a connection, so give it one.
            using var wake = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            wake.Connect(WakeTimeoutMs);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            // It's a background thread, so it ends with the process regardless.
        }

        _listener.Join(WakeTimeoutMs);
    }

    private void Serve(Action<LaunchRequest> onRequest)
    {
        while (!_isStopping)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.CurrentUserOnly);

                pipe.WaitForConnection();
                if (_isStopping)
                {
                    return;
                }

                var request = JsonSerializer.Deserialize(pipe, ChannelJsonContext.Default.LaunchRequest);
                if (request is not null)
                {
                    onRequest(request);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                Log.Warn("A launch request could not be read", ex);
            }
        }
    }
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(LaunchRequest))]
internal sealed partial class ChannelJsonContext : JsonSerializerContext;
