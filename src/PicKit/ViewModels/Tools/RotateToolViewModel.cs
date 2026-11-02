using CommunityToolkit.Mvvm.Input;
using PicKit.Core.Imaging;

namespace PicKit.ViewModels.Tools;

public sealed partial class RotateToolViewModel : ToolViewModel
{
    public RotateToolViewModel(EditorViewModel editor)
        : base(editor)
    {
    }

    public override string Name => "Rotate";

    public override string Glyph => "";

    private Task Transform(string label, OrthoTransform transform)
    {
        var pixels = Editor.Document?.Pixels;
        if (pixels is null)
        {
            return Task.CompletedTask;
        }

        return Editor.ApplyAsync(
            label,
            source => OrthoTransforms.Apply(source, transform),
            () => OrthoTransforms.ToMatrix(transform, pixels.Width, pixels.Height));
    }

    [RelayCommand]
    private Task RotateLeft() => Transform("Rotate left", OrthoTransform.Rotate270);

    [RelayCommand]
    private Task RotateRight() => Transform("Rotate right", OrthoTransform.Rotate90);

    [RelayCommand]
    private Task FlipHorizontal() => Transform("Flip horizontal", OrthoTransform.FlipHorizontal);

    [RelayCommand]
    private Task FlipVertical() => Transform("Flip vertical", OrthoTransform.FlipVertical);
}
