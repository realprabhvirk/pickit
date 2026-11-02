using PicKit.Core.Diagnostics;
using PicKit.Core.Files;
using PicKit.Core.Imaging;

namespace PicKit.Core.Background;

/// <summary>
/// Images that have arrived but haven't been dealt with yet, such as a screenshot nobody clicked
/// on. They wait here until they're saved or trashed, and this folder never syncs anywhere.
/// </summary>
public sealed class ImageInbox
{
    private const string FallbackName = "Dropped image";

    private readonly Action<string> _trash;

    /// <param name="folder">Normally the inbox in PicKit's own data folder.</param>
    /// <param name="trash">Disposes of a file. Without one, the file is simply deleted.</param>
    public ImageInbox(string? folder = null, Action<string>? trash = null)
    {
        Folder = folder ?? AppPaths.Inbox;
        _trash = trash ?? SafeFile.TryDelete;
    }

    public string Folder { get; }

    /// <summary>Everything still waiting, newest first.</summary>
    public IReadOnlyList<string> Waiting
    {
        get
        {
            try
            {
                return Directory.Exists(Folder)
                    ? Directory.EnumerateFiles(Folder).Where(ImageFormats.IsOpenable).OrderByDescending(File.GetLastWriteTimeUtc).ToList()
                    : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warn("Could not read the inbox", ex);
                return [];
            }
        }
    }

    public static string ScreenshotName(DateTime time) => $"Screenshot {time:yyyy-MM-dd HHmmss}.png";

    public bool Contains(string? path) =>
        path is not null
        && string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase);

    public string Add(byte[] data, string name)
    {
        var path = FileNames.MakeUnique(Path.Combine(Folder, FileNames.Sanitize(name)));
        SafeFile.Write(path, stream => stream.Write(data));
        return path;
    }

    /// <summary>
    /// Adds something dragged in, named by what its bytes really are, because neither a web
    /// page's name for a file nor its extension can be trusted. Returns null if it isn't an image.
    /// </summary>
    public string? AddDropped(DroppedFile file)
    {
        if (ImageSniffer.ExtensionFor(file.Data) is not { } extension)
        {
            return null;
        }

        var stem = Path.GetFileNameWithoutExtension(FileNames.Sanitize(file.Name, FallbackName));
        return Add(file.Data, (stem.Length > 0 ? stem : FallbackName) + extension);
    }

    /// <summary>Moves an image out to a real folder and returns where it landed.</summary>
    /// <param name="stem">The name to give it, if it's been renamed, without an extension.</param>
    public string Keep(string path, string folder, string? stem = null)
    {
        Directory.CreateDirectory(folder);
        var name = stem is null ? Path.GetFileName(path) : FileNames.Sanitize(stem) + Path.GetExtension(path);
        var target = FileNames.MakeUnique(Path.Combine(folder, name));
        File.Move(path, target);
        return target;
    }

    public void Trash(string path) => _trash(path);
}
