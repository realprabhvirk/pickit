namespace PicKit.Core.Diagnostics;

public static class AppPaths
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PicKit");

    public static string Data => Root;

    public static string Logs => Path.Combine(Root, "logs");

    public static string Settings => Path.Combine(Root, "settings.json");

    public static string Recent => Path.Combine(Root, "recent.json");

    public static string RenameHistory => Path.Combine(Root, "rename-history.json");

    public static string TempRoot => Path.Combine(Path.GetTempPath(), "PicKit");

    /// <summary>Scratch space for this process, removed on exit and swept on the next start.</summary>
    public static string SessionTemp { get; } = Path.Combine(TempRoot, $"session-{Environment.ProcessId}");
}
