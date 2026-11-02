using System.IO;
using System.Windows;
using Microsoft.Win32;
using PicKit.Core.Imaging;
using PicKit.Views;
using SkiaSharp;

namespace PicKit.Services;

public enum UnsavedChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>Everything that interrupts the user, so view models stay free of windows.</summary>
public class DialogService
{
    public virtual void ShowError(string title, string message, string? details = null, string closeLabel = "OK")
    {
        var dialog = new MessageDialog(title, message, details, closeLabel) { Owner = ActiveWindow() };
        dialog.ShowDialog();
    }

    /// <summary>Asks a yes/no question, returning true only if the confirming button was pressed.</summary>
    public virtual bool Confirm(string title, string message, string confirmLabel, string cancelLabel = "Cancel")
    {
        var dialog = new MessageDialog(title, message, null, cancelLabel, confirmLabel) { Owner = ActiveWindow() };
        dialog.ShowDialog();
        return dialog.Outcome == MessageDialogOutcome.Confirmed;
    }

    public virtual UnsavedChoice AskAboutUnsavedChanges(string fileName)
    {
        var dialog = new MessageDialog(
            "Save your changes?",
            $"{fileName} has changes that haven't been saved.",
            null,
            "Cancel",
            "Save",
            "Don't Save")
        {
            Owner = ActiveWindow(),
        };

        dialog.ShowDialog();
        return dialog.Outcome switch
        {
            MessageDialogOutcome.Confirmed => UnsavedChoice.Save,
            MessageDialogOutcome.Alternate => UnsavedChoice.Discard,
            _ => UnsavedChoice.Cancel,
        };
    }

    public virtual string[] PickImages()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open image",
            Multiselect = true,
            Filter = OpenFilter(),
        };

        return dialog.ShowDialog(ActiveWindow()) == true ? dialog.FileNames : [];
    }

    public virtual string? PickSavePath(string suggestedPath, ImageFormat format)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save image as",
            FileName = Path.GetFileNameWithoutExtension(suggestedPath),
            Filter = "PNG|*.png|JPEG|*.jpg;*.jpeg|WebP|*.webp",
            FilterIndex = format switch
            {
                ImageFormat.Jpeg => 2,
                ImageFormat.WebP => 3,
                _ => 1,
            },
            DefaultExt = ImageFormats.Extension(format),
            AddExtension = true,
            OverwritePrompt = true,
        };

        var folder = Path.GetDirectoryName(suggestedPath);
        if (!string.IsNullOrEmpty(folder))
        {
            dialog.InitialDirectory = folder;
        }

        return dialog.ShowDialog(ActiveWindow()) == true ? dialog.FileName : null;
    }

    /// <summary>Colour to put behind transparent pixels, or null if the user cancelled.</summary>
    public virtual SKColor? ChooseBackground(SKColor initial)
    {
        var dialog = new TransparencyDialog(initial, new ColorService()) { Owner = ActiveWindow() };
        return dialog.ShowDialog() == true ? dialog.SelectedColor : null;
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
