using System.Net.Http;
using PicKit.Core.Diagnostics;

namespace PicKit.Core.Background;

/// <summary>
/// Downloads an image dragged from a web page whose browser passed along only its address.
/// This is the one network request PicKit ever makes, and only when someone drops an image.
/// </summary>
public static class ImageFetcher
{
    private const int MaxBytes = 256 * 1024 * 1024;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>Returns the image, or null if it couldn't be fetched.</summary>
    public static async Task<DroppedFile?> FetchAsync(Uri address)
    {
        if (address.Scheme is not ("http" or "https"))
        {
            return null;
        }

        try
        {
            using var client = new HttpClient { Timeout = Timeout, MaxResponseContentBufferSize = MaxBytes };
            var data = await client.GetByteArrayAsync(address);
            var name = Path.GetFileName(Uri.UnescapeDataString(address.AbsolutePath));
            return new DroppedFile(name, data);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            Log.Warn($"Could not download a dropped image from {address.Host}", ex);
            return null;
        }
    }
}
