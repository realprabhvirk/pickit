using PicKit.Core.Shell;

namespace PicKit;

/// <summary>
/// Collects launch requests as they arrive. Explorer sends one per selected file, so requests for
/// the same action that land close together are merged into a single job.
/// </summary>
public sealed class LaunchInbox
{
    private readonly object _gate = new();
    private readonly List<LaunchRequest> _waiting = [];

    public event Action? RequestArrived;

    public void Add(LaunchRequest request)
    {
        lock (_gate)
        {
            _waiting.Add(request);
        }

        RequestArrived?.Invoke();
    }

    /// <summary>Takes everything waiting, with requests for the same action combined in order.</summary>
    public IReadOnlyList<LaunchRequest> TakeMerged()
    {
        lock (_gate)
        {
            var merged = new List<LaunchRequest>();
            foreach (var request in _waiting)
            {
                if (merged.Count > 0 && merged[^1].Action == request.Action)
                {
                    merged[^1] = merged[^1] with { Paths = [.. merged[^1].Paths, .. request.Paths] };
                }
                else
                {
                    merged.Add(request);
                }
            }

            _waiting.Clear();
            return merged;
        }
    }
}
