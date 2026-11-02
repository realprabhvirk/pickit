using PicKit.Core.Imaging;

namespace PicKit.Core.Shell;

public enum ShellAction
{
    Open,
    Compress,
    Resize,
    Convert,
    Copy,
    RemoveBackground,
    Rename,

    /// <summary>Run by the installer. Adds PicKit to Explorer without opening a window.</summary>
    RegisterMenu,

    /// <summary>Run by the installer. Adds PicKit to "Open with" without opening a window.</summary>
    RegisterOpenWith,

    /// <summary>Run by the uninstaller. Removes everything PicKit added.</summary>
    Unregister,

    /// <summary>Run by the installer. Starts the background helper at each sign-in.</summary>
    RegisterBackground,

    /// <summary>Runs the background helper, which holds the hotkey and the drop box.</summary>
    Background,

    /// <summary>Asks a running background helper to exit.</summary>
    StopBackground,

    /// <summary>Asks a running background helper to take a screenshot.</summary>
    TakeScreenshot,
}

/// <summary>What the app was started to do.</summary>
public sealed record LaunchRequest(ShellAction Action, IReadOnlyList<string> Paths)
{
    private static readonly Dictionary<string, ShellAction> Switches = new(StringComparer.OrdinalIgnoreCase)
    {
        ["--open"] = ShellAction.Open,
        ["--compress"] = ShellAction.Compress,
        ["--resize"] = ShellAction.Resize,
        ["--convert"] = ShellAction.Convert,
        ["--copy"] = ShellAction.Copy,
        ["--remove-background"] = ShellAction.RemoveBackground,
        ["--rename"] = ShellAction.Rename,
        ["--register-menu"] = ShellAction.RegisterMenu,
        ["--register-open-with"] = ShellAction.RegisterOpenWith,
        ["--unregister"] = ShellAction.Unregister,
        ["--register-background"] = ShellAction.RegisterBackground,
        ["--background"] = ShellAction.Background,
        ["--stop-background"] = ShellAction.StopBackground,
        ["--screenshot"] = ShellAction.TakeScreenshot,
    };

    public static string SwitchFor(ShellAction action) => Switches.First(pair => pair.Value == action).Key;

    /// <summary>
    /// Reads a command line. Anything that isn't a known switch is treated as a path, which means
    /// a file name can never be mistaken for an instruction.
    /// </summary>
    public static LaunchRequest Parse(IReadOnlyList<string> arguments)
    {
        var action = ShellAction.Open;
        var paths = new List<string>();
        var onlyPathsRemain = false;

        foreach (var argument in arguments)
        {
            if (!onlyPathsRemain && argument == "--")
            {
                onlyPathsRemain = true;
            }
            else if (!onlyPathsRemain && Switches.TryGetValue(argument, out var parsed))
            {
                action = parsed;
            }
            else if (!string.IsNullOrWhiteSpace(argument))
            {
                paths.Add(argument);
            }
        }

        return new LaunchRequest(action, paths);
    }
}

public static class ImagePaths
{
    private const int MaxFiles = 20_000;

    /// <summary>
    /// Turns dropped or selected paths into image files, looking inside folders. Things that
    /// aren't images are skipped quietly, because a selection often contains other files.
    /// </summary>
    public static IReadOnlyList<string> Expand(IEnumerable<string> paths)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string path)
        {
            if (results.Count < MaxFiles && seen.Add(path))
            {
                results.Add(path);
            }
        }

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                if (ImageFormats.IsOpenable(path))
                {
                    Add(path);
                }
            }
            else if (Directory.Exists(path))
            {
                foreach (var file in EnumerateImages(path))
                {
                    Add(file);
                }
            }
        }

        return results;
    }

    private static IEnumerable<string> EnumerateImages(string folder)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };

        return Directory.EnumerateFiles(folder, "*", options)
            .Where(ImageFormats.IsOpenable)
            .Order(StringComparer.CurrentCultureIgnoreCase);
    }
}
