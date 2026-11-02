using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using PicKit.Core.Imaging;
using PicKit.Views;

namespace PicKit.Services;

/// <summary>Everything that interrupts the user, so view models stay testable and UI-free.</summary>
public sealed class DialogService
{
    public void ShowError(string title, string message, string? details = null, string closeLabel = "OK")
    {
        var dialog = new MessageDialog(title, message, details, closeLabel);
        dialog.Owner = ActiveWindow();
        dialog.ShowDialog();
    }

    /// <summary>Asks a yes/no question, returning true only if the confirming button was pressed.</summary>
    public bool Confirm(string title, string message, string confirmLabel, string cancelLabel = "Cancel")
    {
        var dialog = new MessageDialog(title, message, null, cancelLabel, confirmLabel);
        dialog.Owner = ActiveWindow();
        return dialog.ShowDialog() == true;
    }

    public string[] PickImages()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open image",
            Multiselect = true,
            Filter = OpenFilter(),
        };

        return dialog.ShowDialog(ActiveWindow()) == true ? dialog.FileNames : [];
    }

    private static string OpenFilter()
    {
        var patterns = string.Join(';', ImageFormats.OpenableExtensions.Select(extension => "*" + extension));
        return $"Images|{patterns}|All files|*.*";
    }

    private static Window? ActiveWindow() =>
        Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
        ?? Application.Current?.MainWindow;
}
