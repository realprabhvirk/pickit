namespace PicKit.ViewModels.Tools;

/// <summary>No tool at all: dragging moves around the image, and nothing can change it by accident.</summary>
public sealed class SelectToolViewModel : ToolViewModel
{
    public SelectToolViewModel(EditorViewModel editor)
        : base(editor)
    {
    }

    public override string Name => "Select";

    public override string Glyph => "";

    public override bool HasPanel => false;
}
