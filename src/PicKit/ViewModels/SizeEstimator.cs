using PicKit.Core.Imaging;

namespace PicKit.ViewModels;

/// <summary>
/// Works out a file size by really encoding, a moment after the user stops changing settings.
/// It owns a private copy of the pixels, so it can't outlive the image it's measuring.
/// </summary>
public sealed class SizeEstimator
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(300);

    private CancellationTokenSource? _pending;

    /// <summary>Returns null if a newer request replaced this one, or the estimate failed.</summary>
    public async Task<long?> EstimateAsync(ImageBuffer ownedCopy, Func<ImageBuffer, long> measure)
    {
        Cancel();
        var pending = new CancellationTokenSource();
        _pending = pending;

        try
        {
            await Task.Delay(Delay, pending.Token);
            return await Task.Run(() => measure(ownedCopy), pending.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (ImageException)
        {
            return null;
        }
        finally
        {
            ownedCopy.Dispose();
        }
    }

    public void Cancel()
    {
        _pending?.Cancel();
        _pending = null;
    }
}
