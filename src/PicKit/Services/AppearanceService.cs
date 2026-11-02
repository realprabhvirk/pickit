using System.Windows;
using PicKit.Core.Settings;

namespace PicKit.Services;

public static class AppearanceService
{
    public static void Apply(AppAppearance appearance)
    {
        if (Application.Current is { } application)
        {
            application.ThemeMode = appearance switch
            {
                AppAppearance.Light => ThemeMode.Light,
                AppAppearance.Dark => ThemeMode.Dark,
                _ => ThemeMode.System,
            };
        }
    }
}
