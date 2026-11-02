using System.Text.Json;
using PicKit.Core.Diagnostics;
using PicKit.Core.Files;

namespace PicKit.Core.Settings;

public sealed class SettingsStore
{
    private readonly string _path;

    public SettingsStore(string? path = null)
    {
        _path = path ?? AppPaths.Settings;
        Current = Load();
    }

    public AppSettings Current { get; }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(Current, SettingsJsonContext.Default.AppSettings);
            SafeFile.WriteAllText(_path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not save settings", ex);
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged settings file is not worth refusing to start over.
            Log.Warn("Settings could not be read, using defaults", ex);
        }

        return new AppSettings();
    }
}
