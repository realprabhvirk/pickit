using System.Text.Json;
using PicKit.Core.Diagnostics;
using PicKit.Core.Files;

namespace PicKit.Core.Settings;

/// <summary>A short list of recently opened files, kept only on this PC.</summary>
public sealed class RecentFiles
{
    private const int Limit = 8;

    private readonly string _path;
    private readonly List<string> _files;

    public RecentFiles(string? path = null)
    {
        _path = path ?? AppPaths.Recent;
        _files = Load();
    }

    /// <summary>Most recent first, skipping anything that has since been moved or deleted.</summary>
    public IReadOnlyList<string> Existing => _files.Where(File.Exists).ToList();

    public void Add(string path)
    {
        _files.RemoveAll(existing => string.Equals(existing, path, StringComparison.OrdinalIgnoreCase));
        _files.Insert(0, path);
        if (_files.Count > Limit)
        {
            _files.RemoveRange(Limit, _files.Count - Limit);
        }

        Save();
    }

    public void Clear()
    {
        _files.Clear();
        SafeFile.TryDelete(_path);
    }

    private List<string> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(_path), SettingsJsonContext.Default.ListString) ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn("Recent files could not be read", ex);
        }

        return [];
    }

    private void Save()
    {
        try
        {
            SafeFile.WriteAllText(_path, JsonSerializer.Serialize(_files, SettingsJsonContext.Default.ListString));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Recent files could not be saved", ex);
        }
    }
}
