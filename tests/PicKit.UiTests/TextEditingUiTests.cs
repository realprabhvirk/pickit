using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PicKit.Controls;
using PicKit.Core.Markup;
using PicKit.ViewModels;
using SkiaSharp;

namespace PicKit.UiTests;

public class TextEditingUiTests
{
    private static async Task<EditorViewModel> OpenTextTool(UiHarness harness)
    {
        await TestFiles.OpenPhoto(harness, 1200, 800);
        var editor = harness.ViewModel.Editor;
        editor.ActiveTool = editor.Text;
        harness.Pump();
        return editor;
    }

    private static void ClickCanvas(UiHarness harness, float x, float y)
    {
        var canvas = harness.Window.Canvas;
        var point = TestFiles.ToElement(canvas, new SKPoint(x, y));
        Assert.True(canvas.PressAt(point, MouseButton.Left, ModifierKeys.None));
        canvas.ReleaseAt(point, MouseButton.Left, ModifierKeys.None);
        harness.Pump();
    }

    private static void Type(UiHarness harness, string text)
    {
        harness.Window.TextEntry.Text = text;
        harness.Window.TextEntry.CaretIndex = text.Length;
        harness.Pump();
    }

    private static void MoveFocusTo(UiHarness harness, IInputElement? target)
    {
        harness.Window.TextEntry.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, harness.Window.TextEntry, target)
        {
            RoutedEvent = UIElement.LostKeyboardFocusEvent,
        });
        harness.Pump();
    }

    private static void PressKey(UiHarness harness, Key key)
    {
        var source = PresentationSource.FromVisual(harness.Window)!;
        harness.Window.TextEntry.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key)
        {
            RoutedEvent = UIElement.PreviewKeyDownEvent,
        });
        harness.Pump();
    }

    private static T PanelControl<T>(UiHarness harness, string name)
        where T : FrameworkElement =>
        Descendants(harness.Window.ToolPanel).OfType<T>().First(control =>
            System.Windows.Automation.AutomationProperties.GetName(control) == name);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    [Fact]
    public void Changing_size_font_and_colour_updates_the_box_while_typing() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTextTool(harness);
        var tool = editor.Text;
        ClickCanvas(harness, 300, 300);
        Type(harness, "Hello");

        var box = harness.Window.TextEntry;
        var sizeBefore = box.FontSize;
        var size = PanelControl<Slider>(harness, "Font size");

        // Touching the panel takes focus from the box, which must not end the typing.
        MoveFocusTo(harness, size);
        Assert.NotNull(tool.TextEdit);
        Assert.Equal(Visibility.Visible, box.Visibility);

        size.Value = 96;
        harness.Pump();
        Assert.Equal(sizeBefore * 3, box.FontSize, 3);

        tool.FontFamily = "Georgia";
        tool.ChooseSwatchCommand.Execute(tool.Palette.First(choice => choice.Name == "Blue"));
        harness.Pump();
        Assert.Equal("Georgia", box.FontFamily.Source);
        Assert.Equal(tool.Color, ((SolidColorBrush)box.Foreground).Color);
        harness.Screenshot("text-restyled-while-typing");

        tool.CommitTextEdit(box.Text);
        harness.Pump();
        var text = Assert.IsType<TextElement>(Assert.Single(editor.Document!.Markups));
        Assert.Equal("Hello", text.Text);
        Assert.Equal("Georgia", text.FontFamily);
        Assert.Equal(tool.FontSizeInImagePixels, text.FontSize, 3);
    });

    [Fact]
    public void Text_stays_selected_after_finishing_so_it_can_still_be_changed() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTextTool(harness);
        var tool = editor.Text;
        ClickCanvas(harness, 300, 300);
        Type(harness, "Hello");
        tool.CommitTextEdit(harness.Window.TextEntry.Text);
        harness.Pump();

        var document = editor.Document!;
        Assert.Same(document.Markups[0], tool.Selected);

        tool.FontSize = 64;
        tool.FontSize = 65;
        tool.FontSize = 66;
        harness.Pump();
        Assert.Equal(66 * 1.2f, ((TextElement)document.Markups[0]).FontSize, 3);

        // A slider drag is one edit, however many values it passed through.
        editor.UndoCommand.Execute(null);
        harness.Pump();
        Assert.Equal(32 * 1.2f, ((TextElement)document.Markups[0]).FontSize, 3);
    });

    [Fact]
    public void Clicking_away_while_typing_finishes_without_opening_another_box() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTextTool(harness);
        ClickCanvas(harness, 300, 300);
        Type(harness, "Hello");

        var canvas = harness.Window.Canvas;
        var away = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent };
        canvas.RaiseEvent(away);
        harness.Pump();

        Assert.True(away.Handled);
        Assert.Null(editor.Text.TextEdit);
        Assert.Equal(Visibility.Collapsed, harness.Window.TextEntry.Visibility);
        Assert.Same(editor.Document!.Markups[0], editor.Text.Selected);
    });

    [Fact]
    public void Leaving_for_another_control_still_finishes_the_text() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTextTool(harness);
        ClickCanvas(harness, 300, 300);
        Type(harness, "Hello");

        MoveFocusTo(harness, harness.Window.Canvas);

        Assert.Null(editor.Text.TextEdit);
        Assert.Single(editor.Document!.Markups);
    });

    [Fact]
    public void Delete_with_everything_selected_removes_the_text_in_one_press() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTextTool(harness);
        ClickCanvas(harness, 300, 300);
        Type(harness, "Hello");
        editor.Text.CommitTextEdit(harness.Window.TextEntry.Text);
        harness.Pump();

        // Clicking the text opens it with everything selected.
        ClickCanvas(harness, 320, 310);
        Assert.NotNull(editor.Text.TextEdit);
        Assert.Equal("Hello", harness.Window.TextEntry.SelectedText);

        PressKey(harness, Key.Delete);

        Assert.Null(editor.Text.TextEdit);
        Assert.Equal(Visibility.Collapsed, harness.Window.TextEntry.Visibility);
        Assert.Empty(editor.Document!.Markups);

        editor.UndoCommand.Execute(null);
        harness.Pump();
        Assert.Equal("Hello", ((TextElement)Assert.Single(editor.Document.Markups)).Text);
    });

    [Fact]
    public void Delete_at_the_end_of_new_text_removes_it_and_undo_restores_it() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTextTool(harness);
        ClickCanvas(harness, 300, 300);
        Type(harness, "Hello");

        PressKey(harness, Key.Delete);

        Assert.Null(editor.Text.TextEdit);
        Assert.Empty(editor.Document!.Markups);

        editor.UndoCommand.Execute(null);
        harness.Pump();
        Assert.Equal("Hello", ((TextElement)Assert.Single(editor.Document.Markups)).Text);
    });

    [Fact]
    public void Delete_in_the_middle_of_text_still_removes_one_character() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTextTool(harness);
        ClickCanvas(harness, 300, 300);
        Type(harness, "Hello");
        harness.Window.TextEntry.CaretIndex = 2;

        PressKey(harness, Key.Delete);

        // Not handled here, so the text box deletes the character itself.
        Assert.NotNull(editor.Text.TextEdit);
        Assert.Equal("Hello", harness.Window.TextEntry.Text);
    });

    [Fact]
    public void Delete_key_removes_finished_text_that_is_still_selected() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTextTool(harness);
        ClickCanvas(harness, 300, 300);
        Type(harness, "Hello");
        editor.Text.CommitTextEdit(harness.Window.TextEntry.Text);
        harness.Pump();

        editor.DeleteSelectionCommand.Execute(null);
        harness.Pump();

        Assert.Empty(editor.Document!.Markups);
    });

    [Fact]
    public void Editing_text_hides_the_original_and_escape_brings_it_back() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTextTool(harness);
        ClickCanvas(harness, 300, 300);
        Type(harness, "Hello");
        editor.Text.CommitTextEdit(harness.Window.TextEntry.Text);
        harness.Pump();
        var document = editor.Document!;
        var original = document.Markups[0];
        var undoLabel = document.History.UndoLabel;

        ClickCanvas(harness, 320, 310);
        Assert.Empty(document.Markups);

        PressKey(harness, Key.Escape);

        Assert.Same(original, Assert.Single(document.Markups));
        Assert.Equal(undoLabel, document.History.UndoLabel);
    });

    [Fact]
    public void Text_still_being_typed_goes_into_what_is_saved() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTextTool(harness);
        ClickCanvas(harness, 300, 300);
        Type(harness, "Hello");

        editor.FlushPendingEdits();

        Assert.Equal("Hello", ((TextElement)Assert.Single(editor.Document!.Markups)).Text);
        Assert.Equal(Visibility.Collapsed, harness.Window.TextEntry.Visibility);
    });

    [Fact]
    public void Closing_the_image_while_editing_text_leaves_the_next_image_alone() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTextTool(harness);
        ClickCanvas(harness, 300, 300);
        Type(harness, "Hello");
        editor.Text.CommitTextEdit(harness.Window.TextEntry.Text);
        harness.Pump();
        ClickCanvas(harness, 320, 310);
        Assert.NotNull(editor.Text.TextEdit);

        harness.Dialogs.UnsavedAnswer = PicKit.Services.UnsavedChoice.Discard;
        await TestFiles.OpenPhoto(harness, 900, 600);

        Assert.Null(editor.Text.TextEdit);
        Assert.Empty(editor.Document!.Markups);
    });

    [Fact]
    public void Text_selected_in_markup_shows_font_controls_and_can_be_restyled() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTextTool(harness);
        ClickCanvas(harness, 300, 300);
        Type(harness, "Hello");
        editor.Text.CommitTextEdit(harness.Window.TextEntry.Text);
        harness.Pump();

        editor.ActiveTool = editor.Markup;
        harness.Pump();
        var markup = editor.Markup;
        Assert.False(markup.ShowsFontSize);

        markup.SelectedMode = markup.Modes.First(mode => mode.Label == "Select");
        ClickCanvas(harness, 320, 310);

        Assert.True(markup.ShowsFontSize);
        Assert.False(markup.ShowsThickness);

        markup.FontFamily = "Impact";
        harness.Pump();
        Assert.Equal("Impact", ((TextElement)editor.Document!.Markups[0]).FontFamily);
    });

    [Fact]
    public void Font_presets_are_listed_and_picking_one_selects_it() => UiHarness.Run(async harness =>
    {
        var editor = await OpenTextTool(harness);
        var fonts = PanelControl<ListBox>(harness, "Font");

        Assert.True(fonts.Items.Count >= 8);
        Assert.Equal(TextFonts.Default, fonts.SelectedItem);
        harness.Screenshot("text-font-presets");

        fonts.SelectedItem = "Georgia";
        harness.Pump();
        Assert.Equal("Georgia", editor.Text.FontFamily);
    });
}
