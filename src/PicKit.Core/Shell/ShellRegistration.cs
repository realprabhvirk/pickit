using Microsoft.Win32;

namespace PicKit.Core.Shell;

/// <summary>
/// Adds PicKit to File Explorer using per-user registry entries, so no administrator rights are
/// needed and other accounts on the PC are unaffected. Only keys PicKit owns are ever created or
/// deleted, and the user's chosen default apps are never changed: PicKit offers itself in
/// "Open with" and in Settings, and the choice stays with the user.
/// </summary>
public sealed class ShellRegistration
{
    public const string MenuKey = "PicKit.ContextMenu";
    public const string ProgId = "PicKit.Image";
    private const string ApplicationKey = @"Applications\PicKit.exe";
    private const string CapabilitiesKey = @"PicKit\Capabilities";
    private const string RegisteredApplicationsKey = "RegisteredApplications";
    private const string AppName = "PicKit";

    /// <summary>The verb stays available for up to 100 selected files, Windows' limit for this kind of entry.</summary>
    private const string MultiSelectModel = "Player";

    private static readonly (string Verb, string Label, ShellAction Action)[] MenuItems =
    [
        ("01open", "Open in PicKit", ShellAction.Open),
        ("02compress", "Compress", ShellAction.Compress),
        ("03resize", "Resize", ShellAction.Resize),
        ("04convert", "Convert", ShellAction.Convert),
        ("05copy", "Copy to Clipboard", ShellAction.Copy),
        ("06background", "Remove Background", ShellAction.RemoveBackground),
        ("07rename", "Rename", ShellAction.Rename),
    ];

    /// <summary>The formats PicKit puts itself forward for.</summary>
    private static readonly string[] Extensions =
        [".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff"];

    private readonly RegistryKey _classes;
    private readonly RegistryKey _software;
    private readonly string _executable;

    /// <param name="classes">Normally HKCU\Software\Classes.</param>
    /// <param name="software">Normally HKCU\Software.</param>
    /// <param name="executable">Full path to PicKit.exe.</param>
    public ShellRegistration(RegistryKey classes, RegistryKey software, string executable)
    {
        _classes = classes;
        _software = software;
        _executable = executable;
    }

    public bool IsMenuRegistered
    {
        get
        {
            using var key = _classes.OpenSubKey(MenuKey);
            return key is not null;
        }
    }

    public bool IsOpenWithRegistered
    {
        get
        {
            using var key = _classes.OpenSubKey(ProgId);
            return key is not null;
        }
    }

    public static ShellRegistration ForCurrentUser(string executable)
    {
        return new ShellRegistration(
            Registry.CurrentUser.CreateSubKey(@"Software\Classes"),
            Registry.CurrentUser.CreateSubKey("Software"),
            executable);
    }

    /// <summary>Adds the right-click menu on image files.</summary>
    public void RegisterMenu()
    {
        foreach (var (verb, label, action) in MenuItems)
        {
            using var key = _classes.CreateSubKey($@"{MenuKey}\shell\{verb}");
            key.SetValue(null, label);
            key.SetValue("MultiSelectModel", MultiSelectModel);
            using var command = key.CreateSubKey("command");
            command.SetValue(null, CommandFor(action));
        }

        foreach (var extension in Extensions)
        {
            using var key = _classes.CreateSubKey($@"SystemFileAssociations\{extension}\shell\PicKit");
            key.SetValue("MUIVerb", AppName);
            key.SetValue("Icon", $"\"{_executable}\",0");
            key.SetValue("ExtendedSubCommandsKey", MenuKey);
            key.SetValue("MultiSelectModel", MultiSelectModel);
        }
    }

    /// <summary>Lists PicKit in "Open with" and in Settings, without becoming anyone's default.</summary>
    public void RegisterOpenWith()
    {
        using (var progId = _classes.CreateSubKey(ProgId))
        {
            progId.SetValue(null, "PicKit image");
            using var icon = progId.CreateSubKey("DefaultIcon");
            icon.SetValue(null, $"\"{_executable}\",0");
            using var command = progId.CreateSubKey(@"shell\open\command");
            command.SetValue(null, $"\"{_executable}\" \"%1\"");
        }

        using (var application = _classes.CreateSubKey(ApplicationKey))
        {
            application.SetValue("FriendlyAppName", AppName);
            using var command = application.CreateSubKey(@"shell\open\command");
            command.SetValue(null, $"\"{_executable}\" \"%1\"");
            using var types = application.CreateSubKey("SupportedTypes");
            foreach (var extension in Extensions)
            {
                types.SetValue(extension, string.Empty);
            }
        }

        using (var capabilities = _software.CreateSubKey(CapabilitiesKey))
        {
            capabilities.SetValue("ApplicationName", AppName);
            capabilities.SetValue("ApplicationDescription", "The simple image toolbox for Windows.");
            using var associations = capabilities.CreateSubKey("FileAssociations");
            foreach (var extension in Extensions)
            {
                associations.SetValue(extension, ProgId);
            }
        }

        using (var registered = _software.CreateSubKey(RegisteredApplicationsKey))
        {
            registered.SetValue(AppName, $@"Software\{CapabilitiesKey}");
        }

        foreach (var extension in Extensions)
        {
            using var openWith = _classes.CreateSubKey($@"{extension}\OpenWithProgids");
            openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
        }
    }

    public void UnregisterMenu()
    {
        foreach (var extension in Extensions)
        {
            _classes.DeleteSubKeyTree($@"SystemFileAssociations\{extension}\shell\PicKit", throwOnMissingSubKey: false);
            DeleteIfEmpty(_classes, $@"SystemFileAssociations\{extension}\shell");
            DeleteIfEmpty(_classes, $@"SystemFileAssociations\{extension}");
        }

        DeleteIfEmpty(_classes, "SystemFileAssociations");
        _classes.DeleteSubKeyTree(MenuKey, throwOnMissingSubKey: false);
    }

    public void UnregisterOpenWith()
    {
        foreach (var extension in Extensions)
        {
            using (var openWith = _classes.OpenSubKey($@"{extension}\OpenWithProgids", writable: true))
            {
                openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
            }

            DeleteIfEmpty(_classes, $@"{extension}\OpenWithProgids");
            DeleteIfEmpty(_classes, extension);
        }

        using (var registered = _software.OpenSubKey(RegisteredApplicationsKey, writable: true))
        {
            registered?.DeleteValue(AppName, throwOnMissingValue: false);
        }

        _software.DeleteSubKeyTree(CapabilitiesKey, throwOnMissingSubKey: false);
        DeleteIfEmpty(_software, "PicKit");
        _classes.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);
        _classes.DeleteSubKeyTree(ApplicationKey, throwOnMissingSubKey: false);
        DeleteIfEmpty(_classes, "Applications");
    }

    public void UnregisterAll()
    {
        UnregisterMenu();
        UnregisterOpenWith();
    }

    private static void DeleteIfEmpty(RegistryKey root, string path)
    {
        using (var key = root.OpenSubKey(path))
        {
            if (key is null || key.SubKeyCount > 0 || key.ValueCount > 0)
            {
                return;
            }
        }

        root.DeleteSubKey(path, throwOnMissingSubKey: false);
    }

    private string CommandFor(ShellAction action) =>
        $"\"{_executable}\" {LaunchRequest.SwitchFor(action)} -- \"%1\"";
}
