using CommunityToolkit.Mvvm.ComponentModel;
using PicKit.Core.Documents;

namespace PicKit.ViewModels;

/// <summary>
/// One open image, with its own edits and undo history. Its picture loads the first time it's
/// shown, so opening a large selection doesn't hold every image in memory at once.
/// </summary>
public sealed class DocumentTab : ObservableObject
{
    private const string BlankName = "New tab";

    public DocumentTab(string? path = null, EditorDocument? document = null)
    {
        Path = path;
        Document = document;
    }

    /// <summary>The file behind this tab, or null for a blank tab or a pasted image.</summary>
    public string? Path { get; set; }

    public EditorDocument? Document { get; set; }

    /// <summary>The inbox file behind the image, while it hasn't been saved or trashed.</summary>
    public string? WaitingPath { get; set; }

    public bool IsBlank => Path is null && Document is null;

    public bool IsDirty => Document?.IsDirty == true;

    public string Name => Document?.Source.DisplayName
        ?? (Path is null ? BlankName : System.IO.Path.GetFileName(Path));

    public string Title => IsDirty ? $"● {Name}" : Name;

    public void Refresh()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Title));
    }
}
