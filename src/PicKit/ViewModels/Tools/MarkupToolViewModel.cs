using System.Collections.Immutable;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PicKit.Controls;
using PicKit.Controls.Tools;
using PicKit.Core.Markup;
using PicKit.Services;
using SkiaSharp;

namespace PicKit.ViewModels.Tools;

public sealed record MarkupModeChoice(MarkupMode Mode, string Label, string Glyph);

public sealed record ColorChoice(string Name, Color Color)
{
    public SolidColorBrush Brush { get; } = new(Color);
}

/// <summary>A piece of text being typed on the canvas, before it becomes markup.</summary>
public sealed record TextEditSession(SKPoint Position, TextElement? Existing);

/// <summary>
/// Shared by the Markup, Text and Highlight tools, which differ only in which modes they offer.
/// Sizes are expressed relative to the image, so "medium" looks the same on a small screenshot
/// and on a 50-megapixel photo.
/// </summary>
public sealed partial class MarkupToolViewModel : ToolViewModel, IMarkupHost
{
    private const float ReferenceSide = 1000;

    private readonly ColorService _colors;
    private readonly MarkupCanvasTool _canvasTool;
    private readonly bool _showsOpacity;
    private ImmutableArray<MarkupElement> _elementsBeforeMove;
    private bool _isMovingSelection;
    private bool _isApplyingSelectionStyle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mode))]
    [NotifyPropertyChangedFor(nameof(ShowsFontSize))]
    [NotifyPropertyChangedFor(nameof(ShowsThickness))]
    [NotifyPropertyChangedFor(nameof(Hint))]
    private MarkupModeChoice _selectedMode;

    [ObservableProperty]
    private Color _color;

    [ObservableProperty]
    private double _thickness;

    [ObservableProperty]
    private double _opacity;

    [ObservableProperty]
    private double _fontSize = 32;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private MarkupElement? _selected;

    [ObservableProperty]
    private TextEditSession? _textEdit;

    public MarkupToolViewModel(
        EditorViewModel editor,
        ColorService colors,
        string name,
        string glyph,
        IReadOnlyList<MarkupModeChoice> modes,
        Color defaultColor,
        double defaultThickness,
        double defaultOpacity,
        bool showsOpacity = true)
        : base(editor)
    {
        _colors = colors;
        Name = name;
        Glyph = glyph;
        Modes = modes;
        _selectedMode = modes[0];
        _color = defaultColor;
        _thickness = defaultThickness;
        _opacity = defaultOpacity;
        _showsOpacity = showsOpacity;
        _canvasTool = new MarkupCanvasTool(this);
    }

    public override string Name { get; }

    public override string Glyph { get; }

    public override ICanvasTool CanvasTool => _canvasTool;

    public IReadOnlyList<MarkupModeChoice> Modes { get; }

    public bool ShowsModes => Modes.Count > 1;

    public IReadOnlyList<ColorChoice> Palette { get; } =
    [
        new("Red", Color.FromRgb(0xE8, 0x11, 0x23)),
        new("Orange", Color.FromRgb(0xF7, 0x63, 0x0C)),
        new("Yellow", Color.FromRgb(0xFF, 0xD8, 0x00)),
        new("Green", Color.FromRgb(0x10, 0x89, 0x3E)),
        new("Blue", Color.FromRgb(0x00, 0x78, 0xD4)),
        new("Purple", Color.FromRgb(0x88, 0x17, 0x98)),
        new("Black", Color.FromRgb(0x00, 0x00, 0x00)),
        new("White", Color.FromRgb(0xFF, 0xFF, 0xFF)),
    ];

    public MarkupMode Mode => SelectedMode.Mode;

    public bool ShowsFontSize => Mode == MarkupMode.Text;

    public bool ShowsThickness => Mode != MarkupMode.Text;

    public bool ShowsOpacity => _showsOpacity;

    public bool HasSelection => Selected is not null;

    public string Hint => Mode switch
    {
        MarkupMode.Text => "Click where the text should go. Ctrl+Enter or click away to finish.",
        MarkupMode.Highlight => "Drag over what you want to highlight.",
        MarkupMode.Select => "Click an item to select it, then drag to move it.",
        _ => "Hold Shift for straight lines and perfect shapes.",
    };

    public MarkupStyle CurrentStyle => new(ToSkia(Color), (float)Thickness * ImageUnit, (float)Opacity);

    /// <summary>Text size in image pixels for the current slider position.</summary>
    public float FontSizeInImagePixels => (float)FontSize * ImageUnit;

    public IReadOnlyList<MarkupElement> Elements =>
        Editor.Document is { } document ? document.Markups : [];

    private float ImageUnit
    {
        get
        {
            var pixels = Editor.Document?.Pixels;
            return pixels is null ? 1 : Math.Max(1, Math.Max(pixels.Width, pixels.Height) / ReferenceSide);
        }
    }

    public override void Deactivate()
    {
        CommitTextEdit(TextEdit?.Existing?.Text ?? string.Empty, cancel: true);
        Selected = null;
    }

    public void Commit(MarkupElement element)
    {
        if (Editor.Document is { } document)
        {
            document.SetMarkups($"Add {Name.ToLowerInvariant()}", document.Markups.Add(element));
        }
    }

    public void Select(MarkupElement? element)
    {
        // The drag that moves an element is recorded as one edit, however many events it took.
        FinishMove();
        Selected = element;

        if (element is not null)
        {
            _isApplyingSelectionStyle = true;
            Color = FromSkia(element.Style.Color);
            Opacity = element.Style.Opacity;
            Thickness = Math.Clamp(element.Style.Thickness / ImageUnit, 1, 40);
            if (element is TextElement text)
            {
                FontSize = Math.Clamp(text.FontSize / ImageUnit, 8, 200);
            }

            _isApplyingSelectionStyle = false;
        }
    }

    public void MoveSelected(float deltaX, float deltaY)
    {
        if (Selected is not { } element || Editor.Document is not { } document)
        {
            return;
        }

        if (!_isMovingSelection)
        {
            _isMovingSelection = true;
            _elementsBeforeMove = document.Markups;
        }

        var moved = element.Moved(deltaX, deltaY);
        document.PreviewMarkups(document.Markups.Replace(element, moved));
        Selected = moved;
    }

    /// <summary>Records a finished drag as a single undoable step.</summary>
    public void FinishMove()
    {
        if (_isMovingSelection && Editor.Document is { } document)
        {
            _isMovingSelection = false;
            var final = document.Markups;
            document.PreviewMarkups(_elementsBeforeMove);
            document.SetMarkups("Move markup", final);
        }
    }

    public void BeginText(SKPoint position, TextElement? existing)
    {
        if (existing is not null)
        {
            Select(existing);
        }

        TextEdit = new TextEditSession(existing?.Position ?? position, existing);
    }

    /// <summary>Ends typing. Empty text adds nothing, or removes the element if it was being edited.</summary>
    public void CommitTextEdit(string text, bool cancel = false)
    {
        if (TextEdit is not { } session || Editor.Document is not { } document)
        {
            return;
        }

        TextEdit = null;
        if (cancel)
        {
            return;
        }

        var hasText = !string.IsNullOrWhiteSpace(text);
        if (session.Existing is { } existing)
        {
            var updated = hasText
                ? document.Markups.Replace(existing, existing with { Text = text, Style = CurrentStyle, FontSize = FontSizeInImagePixels })
                : document.Markups.Remove(existing);

            document.SetMarkups("Edit text", updated);
            Selected = null;
        }
        else if (hasText)
        {
            Commit(new TextElement
            {
                Style = CurrentStyle,
                Position = session.Position,
                Text = text,
                FontSize = FontSizeInImagePixels,
            });
        }
    }

    /// <summary>Delete key: removes the selected element.</summary>
    public bool DeleteSelected()
    {
        if (Selected is { } element && Editor.Document is { } document)
        {
            document.SetMarkups("Delete markup", document.Markups.Remove(element));
            Selected = null;
            return true;
        }

        return false;
    }

    public override void OnImageChanged()
    {
        Selected = null;
        CommitTextEdit(string.Empty, cancel: true);
    }

    partial void OnColorChanged(Color value) => RestyleSelection();

    partial void OnThicknessChanged(double value) => RestyleSelection();

    partial void OnOpacityChanged(double value) => RestyleSelection();

    partial void OnFontSizeChanged(double value) => RestyleSelection();

    partial void OnSelectedModeChanged(MarkupModeChoice value)
    {
        if (value.Mode != MarkupMode.Select)
        {
            Selected = null;
        }
    }

    private void RestyleSelection()
    {
        if (_isApplyingSelectionStyle || Selected is not { } element || Editor.Document is not { } document)
        {
            return;
        }

        var restyled = element is TextElement text
            ? text with { Style = CurrentStyle, FontSize = FontSizeInImagePixels }
            : element.Restyled(CurrentStyle);

        document.SetMarkups("Change markup", document.Markups.Replace(element, restyled));
        Selected = restyled;
    }

    private static SKColor ToSkia(Color color) => new(color.R, color.G, color.B);

    private static Color FromSkia(SKColor color) => Color.FromRgb(color.Red, color.Green, color.Blue);

    [RelayCommand]
    private void ChooseSwatch(ColorChoice choice) => Color = choice.Color;

    [RelayCommand]
    private void ChooseCustomColor()
    {
        if (_colors.Pick(Color) is { } picked)
        {
            Color = picked;
        }
    }
}
