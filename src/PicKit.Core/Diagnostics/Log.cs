using System.Text;

namespace PicKit.Core.Diagnostics;

/// <summary>A small local log for diagnosing problems. Nothing here ever leaves the PC.</summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;

    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppPaths.Logs, "pickit.log");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            var line = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                .Append(' ').Append(level).Append(' ').AppendLine(message);

            if (exception is not null)
            {
                line.AppendLine(exception.ToString());
            }

            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Logs);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                {
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                }

                File.AppendAllText(FilePath, line.ToString());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never take the app down.
        }
    }
}
