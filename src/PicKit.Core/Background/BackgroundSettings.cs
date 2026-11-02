using System.Text.Json;
using PicKit.Core.Diagnostics;
using PicKit.Core.Files;
using PicKit.Core.Settings;

namespace PicKit.Core.Background;

/// <summary>
/// Settings shared with the background helper. They have their own file because the app and
/// the helper run side by side, and one saving its settings mustn't undo a change by the other.
/// </summary>
public sealed class BackgroundSettings
{
    public bool ShowDropBox { get; set; } = true;

    /// <summary>Where the drop box sits, in screen pixels, or null for the default spot.</summary>
    public int? DropBoxLeft { get; set; }

    public int? DropBoxTop { get; set; }

    /// <summary>Where saved screenshots go, or null for the default folder.</summary>
    public string? SaveFolder { get; set; }
}

public sealed class BackgroundSettingsStore
{
    private readonly string _path;

    public BackgroundSettingsStore(string? path = null)
    {
        _path = path ?? AppPaths.BackgroundSettings;
    }

    /// <summary>Always reads the file, so each process sees what the other last saved.</summary>
    public BackgroundSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize(File.ReadAllText(_path), SettingsJsonContext.Default.BackgroundSettings)
                    ?? new BackgroundSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warn("Background settings could not be read, using defaults", ex);
        }

        return new BackgroundSettings();
    }

    public void Update(Action<BackgroundSettings> change)
    {
        var settings = Load();
        change(settings);
        try
        {
            SafeFile.WriteAllText(_path, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.BackgroundSettings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("Could not save background settings", ex);
        }
    }

    public string SaveFolder => Load().SaveFolder is { Length: > 0 } folder ? folder : AppPaths.DefaultSaveFolder;
}
