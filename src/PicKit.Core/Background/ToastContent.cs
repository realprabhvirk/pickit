using System.Security;
using PicKit.Core.Files;
using PicKit.Core.Shell;

namespace PicKit.Core.Background;

/// <summary>What a Windows notification says. The Windows side of it is kept out of here.</summary>
public static class ToastContent
{
    /// <summary>The name Windows files these notifications under.</summary>
    public const string AppId = "PicKit.Screenshots";

    /// <summary>The notification's markup. It's always silent, because a snip should make no sound.</summary>
    /// <param name="imagePath">A picture to show in it, if there is one.</param>
    /// <param name="link">What clicking it opens. That works even after PicKit has closed.</param>
    public static string Build(string title, string detail, string? imagePath = null, string? link = null)
    {
        var activation = link is null ? string.Empty : $" activationType=\"protocol\" launch=\"{SecurityElement.Escape(link)}\"";
        var image = imagePath is null
            ? string.Empty
            : $"<image placement=\"hero\" src=\"{SecurityElement.Escape(new Uri(imagePath).AbsoluteUri)}\"/>";

        return $"<toast{activation}><visual><binding template=\"ToastGeneric\">"
            + $"<text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(detail)}</text>{image}"
            + "</binding></visual><audio silent=\"true\"/></toast>";
    }
}

/// <summary>
/// The address behind a screenshot notification. Any web page can ask Windows to open a
/// registered address, so it carries only a file name, and that's checked before it's used.
/// </summary>
public static class ScreenshotLink
{
    public const string Scheme = "pickit";

    private const string Prefix = Scheme + ":screenshot?name=";

    public static string For(string fileName) => Prefix + Uri.EscapeDataString(fileName);

    /// <summary>The file name in a link, or null if it isn't one PicKit made.</summary>
    public static string? NameIn(string link)
    {
        if (!link.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var name = Uri.UnescapeDataString(link[Prefix.Length..]);
        var isPlainName = name.Length > 0 && name == Path.GetFileName(name) && name == FileNames.Sanitize(name);
        return isPlainName && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? name : null;
    }

    /// <summary>
    /// Works out what a click means: save the screenshot if it's still waiting, then open it.
    /// If it was already saved, that copy opens. If it's gone, the app just opens.
    /// </summary>
    public static LaunchRequest Resolve(string link, ImageInbox inbox, string saveFolder)
    {
        var open = new LaunchRequest(ShellAction.Open, []);
        if (NameIn(link) is not { } name)
        {
            return open;
        }

        var waiting = Path.Combine(inbox.Folder, name);
        if (File.Exists(waiting))
        {
            try
            {
                return open with { Paths = [inbox.Keep(waiting, saveFolder)] };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return open;
            }
        }

        var saved = Path.Combine(saveFolder, name);
        return File.Exists(saved) ? open with { Paths = [saved] } : open;
    }
}
