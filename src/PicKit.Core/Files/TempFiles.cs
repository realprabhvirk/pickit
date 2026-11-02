using PicKit.Core.Diagnostics;

namespace PicKit.Core.Files;

public static class TempFiles
{
    public static string NewPath(string extension = ".tmp")
    {
        Directory.CreateDirectory(AppPaths.SessionTemp);
        return Path.Combine(AppPaths.SessionTemp, Guid.NewGuid().ToString("N") + extension);
    }

    public static void DeleteSessionFolder()
    {
        try
        {
            if (Directory.Exists(AppPaths.SessionTemp))
            {
                Directory.Delete(AppPaths.SessionTemp, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not remove the session temp folder", ex);
        }
    }

    /// <summary>Removes folders left behind if an earlier session crashed.</summary>
    public static void SweepAbandonedSessions()
    {
        try
        {
            if (!Directory.Exists(AppPaths.TempRoot))
            {
                return;
            }

            foreach (var folder in Directory.EnumerateDirectories(AppPaths.TempRoot, "session-*"))
            {
                if (!string.Equals(folder, AppPaths.SessionTemp, StringComparison.OrdinalIgnoreCase)
                    && !IsOwnerAlive(folder))
                {
                    Directory.Delete(folder, recursive: true);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not sweep old temp folders", ex);
        }
    }

    private static bool IsOwnerAlive(string folder)
    {
        var name = Path.GetFileName(folder);
        if (!int.TryParse(name.AsSpan("session-".Length), out var processId))
        {
            return false;
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return string.Equals(process.ProcessName, "PicKit", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
