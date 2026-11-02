namespace PicKit.Core.Diagnostics;

public static class AppPaths
{
    /// <summary>Tests point this at a scratch folder, so they never touch the user's own data.</summary>
    public static string Data { get; internal set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PicKit");

    public static string Logs => Path.Combine(Data, "logs");

    public static string Settings => Path.Combine(Data, "settings.json");

    public static string Recent => Path.Combine(Data, "recent.json");

    public static string RenameHistory => Path.Combine(Data, "rename-history.json");

    public static string TempRoot => Path.Combine(Path.GetTempPath(), "PicKit");

    /// <summary>Scratch space for this process, removed on exit and swept on the next start.</summary>
    public static string SessionTemp { get; } = Path.Combine(TempRoot, $"session-{Environment.ProcessId}");
}
