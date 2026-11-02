using CommunityToolkit.Mvvm.ComponentModel;
using PicKit.Controls;

namespace PicKit.ViewModels;

/// <summary>One tool: its entry in the rail, its contextual panel, and its behaviour over the canvas.</summary>
public abstract class ToolViewModel : ObservableObject
{
    protected ToolViewModel(EditorViewModel editor)
    {
        Editor = editor;
    }

    public abstract string Name { get; }

    /// <summary>Segoe Fluent Icons code point for the rail.</summary>
    public abstract string Glyph { get; }

    public virtual string? ShortcutHint => null;

    public virtual ICanvasTool? CanvasTool => null;

    protected EditorViewModel Editor { get; }

    public virtual void Activate()
    {
    }

    /// <summary>Called when the user leaves the tool. Anything unapplied is dropped.</summary>
    public virtual void Deactivate()
    {
    }

    /// <summary>The image changed underneath the tool, for example through undo.</summary>
    public virtual void OnImageChanged()
    {
    }

    /// <summary>Enter was pressed. Returns true if the tool had something to apply.</summary>
    public virtual bool ApplyPending() => false;

    /// <summary>Escape was pressed. Returns true if that dropped something, keeping the tool open.</summary>
    public virtual bool CancelPending() => false;

    /// <summary>Called before any export, so the file matches what is on screen.</summary>
    public virtual void FlushPending()
    {
    }
}
