namespace PicKit.Core.Files;

/// <summary>
/// Writes go to a temporary file beside the target and are swapped in only once complete,
/// so a crash or full disk can never leave the user's original half-written.
/// </summary>
public static class SafeFile
{
    public static void Write(string path, Action<Stream> writeContent)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new IOException($"{path} has no parent folder.");

        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, $".pickit-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                writeContent(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    public static void WriteAllText(string path, string content) =>
        Write(path, stream =>
        {
            using var writer = new StreamWriter(stream, leaveOpen: true);
            writer.Write(content);
        });

    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temporary files are swept on the next start.
        }
    }
}
