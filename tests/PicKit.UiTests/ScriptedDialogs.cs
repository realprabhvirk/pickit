using PicKit.Core.Imaging;
using PicKit.Services;
using SkiaSharp;

namespace PicKit.UiTests;

/// <summary>Answers dialogs from the test, and records what the user would have been asked.</summary>
internal sealed class ScriptedDialogs : DialogService
{
    public List<string> Errors { get; } = [];

    public List<string> Confirmations { get; } = [];

    public int UnsavedPrompts { get; private set; }

    public int BackgroundPrompts { get; private set; }

    public bool ConfirmAnswer { get; set; } = true;

    public UnsavedChoice UnsavedAnswer { get; set; } = UnsavedChoice.Cancel;

    public string? SavePath { get; set; }

    public string? FolderPath { get; set; }

    public SKColor? BackgroundAnswer { get; set; } = SKColors.White;

    public override void ShowError(string title, string message, string? details = null, string closeLabel = "OK") =>
        Errors.Add(title);

    public override bool Confirm(string title, string message, string confirmLabel, string cancelLabel = "Cancel")
    {
        Confirmations.Add(message);
        return ConfirmAnswer;
    }

    public override UnsavedChoice AskAboutUnsavedChanges(string fileName)
    {
        UnsavedPrompts++;
        return UnsavedAnswer;
    }

    public override string? PickSavePath(string suggestedPath, ImageFormat format) => SavePath;

    public override string? PickFolder() => FolderPath;

    public override SKColor? ChooseBackground(SKColor initial)
    {
        BackgroundPrompts++;
        return BackgroundAnswer;
    }
}
