using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Core.Diagnostics;
using PicKit.Core.Settings;
using PicKit.Core.Shell;
using PicKit.Services;
using SkiaSharp;

namespace PicKit.ViewModels;

public sealed record ColorOption(string Label, string Value);

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly SettingsStore _settings;
    private readonly RecentFiles _recent;
    private readonly ShellRegistration _shell;
    private readonly Action _recentCleared;

    [ObservableProperty]
    private bool _confirmOverwrite;

    [ObservableProperty]
    private bool _rememberWindow;

    [ObservableProperty]
    private AppAppearance _appearance;

    [ObservableProperty]
    private double _jpegQuality;

    [ObservableProperty]
    private double _webPQuality;

    [ObservableProperty]
    private string _defaultFormat;

    [ObservableProperty]
    private ColorOption _background;

    [ObservableProperty]
    private bool _keepPhotoDetails;

    [ObservableProperty]
    private bool _explorerMenu;

    [ObservableProperty]
    private bool _openWith;

    [ObservableProperty]
    private string? _statusText;

    public SettingsViewModel(SettingsStore settings, RecentFiles recent, ShellRegistration shell, Action recentCleared)
    {
        _settings = settings;
        _recent = recent;
        _shell = shell;
        _recentCleared = recentCleared;

        var current = settings.Current;
        _confirmOverwrite = current.ConfirmOverwrite;
        _rememberWindow = current.RememberWindow;
        _appearance = current.Appearance;
        _jpegQuality = current.JpegQuality;
        _webPQuality = current.WebPQuality;
        _defaultFormat = current.DefaultExportFormat;
        _background = Backgrounds.FirstOrDefault(option => option.Value == current.TransparencyBackground) ?? Backgrounds[0];
        _keepPhotoDetails = current.MetadataHandling == MetadataHandling.Keep;
        _explorerMenu = shell.IsMenuRegistered;
        _openWith = shell.IsOpenWithRegistered;
    }

    public IReadOnlyList<string> Formats { get; } = ["Png", "Jpeg", "WebP"];

    public IReadOnlyList<ColorOption> Backgrounds { get; } =
    [
        new("White", "#FFFFFF"),
        new("Black", "#000000"),
    ];

    /// <summary>Keeps the stored setting in step with each control as it changes.</summary>
    partial void OnConfirmOverwriteChanged(bool value) => Update(settings => settings.ConfirmOverwrite = value);

    partial void OnRememberWindowChanged(bool value) => Update(settings => settings.RememberWindow = value);

    partial void OnJpegQualityChanged(double value) => Update(settings => settings.JpegQuality = (int)value);

    partial void OnWebPQualityChanged(double value) => Update(settings => settings.WebPQuality = (int)value);

    partial void OnDefaultFormatChanged(string value) => Update(settings => settings.DefaultExportFormat = value);

    partial void OnBackgroundChanged(ColorOption value) => Update(settings => settings.TransparencyBackground = value.Value);

    partial void OnKeepPhotoDetailsChanged(bool value) =>
        Update(settings => settings.MetadataHandling = value ? MetadataHandling.Keep : MetadataHandling.Remove);

    partial void OnAppearanceChanged(AppAppearance value)
    {
        Update(settings => settings.Appearance = value);
        AppearanceService.Apply(value);
    }

    partial void OnExplorerMenuChanged(bool value) =>
        ChangeIntegration(value ? _shell.RegisterMenu : _shell.UnregisterMenu);

    partial void OnOpenWithChanged(bool value) =>
        ChangeIntegration(value ? _shell.RegisterOpenWith : _shell.UnregisterOpenWith);

    private void Update(Action<AppSettings> change)
    {
        change(_settings.Current);
        _settings.Save();
    }

    private void ChangeIntegration(Action change)
    {
        try
        {
            change();
            ShellNotifications.AssociationsChanged();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            Log.Warn("Could not change Explorer integration", ex);
            StatusText = "PicKit couldn't change this setting.";
        }
    }

    [RelayCommand]
    private void ClearRecent()
    {
        _recent.Clear();
        _recentCleared();
        StatusText = "Recent files cleared.";
    }
}
