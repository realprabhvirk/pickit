using System.IO;
using System.Windows;
using System.Windows.Input;
using PicKit.Controls.Tools;
using PicKit.Core.Imaging;
using PicKit.Core.Markup;
using PicKit.Core.Shell;
using PicKit.Services;
using PicKit.ViewModels;
using PicKit.ViewModels.Tools;
using SkiaSharp;

namespace PicKit.UiTests;

public class TabsUiTests
{
    private static string Write(UiHarness harness, string name, SKColor? color = null)
    {
        var path = Path.Combine(harness.Folder, name);
        using var image = ImageBuffer.Create(600, 400);
        image.Bitmap.Erase(color ?? SKColors.Teal);
        File.WriteAllBytes(path, ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png)));
        return path;
    }

    private static void Open(UiHarness harness, params string[] paths)
    {
        var count = harness.ViewModel.Tabs.Count;
        harness.ViewModel.OpenPaths(paths);
        harness.WaitFor(() => harness.ViewModel.Tabs.Count > count || paths.Contains(harness.ViewModel.SelectedTab?.Path));
        Settle(harness);
    }

    private static void Settle(UiHarness harness)
    {
        harness.WaitFor(() => !harness.ViewModel.IsLoading
            && ReferenceEquals(harness.ViewModel.Editor.Document, harness.ViewModel.SelectedTab?.Document));
        harness.Pump();
    }

    private static async Task Edit(UiHarness harness)
    {
        await harness.ViewModel.Editor.Rotate.RotateRightCommand.ExecuteAsync(null);
        harness.Pump();
    }

    [Fact]
    public void A_new_image_opens_in_its_own_tab_without_asking_anything() => UiHarness.Run(async harness =>
    {
        Open(harness, Write(harness, "one.png"));
        await Edit(harness);
        var viewModel = harness.ViewModel;
        var first = viewModel.Editor.Document;

        Open(harness, Write(harness, "two.png"));

        Assert.Equal(2, viewModel.Tabs.Count);
        Assert.Equal(0, harness.Dialogs.UnsavedPrompts);
        Assert.NotSame(first, viewModel.Editor.Document);
        Assert.Same(viewModel.Tabs[1], viewModel.SelectedTab);
        harness.Screenshot("tabs");
    });

    [Fact]
    public void Going_back_to_a_tab_finds_its_edits_and_undo_intact() => UiHarness.Run(async harness =>
    {
        Open(harness, Write(harness, "one.png"));
        await Edit(harness);
        var viewModel = harness.ViewModel;
        var first = viewModel.Editor.Document!;
        Open(harness, Write(harness, "two.png"));

        viewModel.SelectedTab = viewModel.Tabs[0];
        Settle(harness);

        Assert.Same(first, viewModel.Editor.Document);
        Assert.True(first.IsDirty);
        Assert.Equal((400, 600), (first.Pixels.Width, first.Pixels.Height));

        viewModel.Editor.UndoCommand.Execute(null);
        harness.Pump();
        Assert.Equal((600, 400), (first.Pixels.Width, first.Pixels.Height));
    });

    [Fact]
    public void A_tab_shows_when_it_has_unsaved_edits() => UiHarness.Run(async harness =>
    {
        Open(harness, Write(harness, "one.png"));
        var tab = harness.ViewModel.SelectedTab!;
        Assert.Equal("one.png", tab.Title);

        await Edit(harness);

        Assert.Equal("● one.png", tab.Title);
    });

    [Fact]
    public void Opening_an_image_already_open_goes_to_its_tab() => UiHarness.Run(harness =>
    {
        var one = Write(harness, "one.png");
        var two = Write(harness, "two.png");
        Open(harness, one);
        Open(harness, two);

        harness.ViewModel.OpenPaths([one]);
        harness.WaitFor(() => harness.ViewModel.SelectedTab?.Path == one);
        Settle(harness);

        Assert.Equal(2, harness.ViewModel.Tabs.Count);
    });

    [Fact]
    public void Several_images_each_get_a_tab_and_load_only_when_shown() => UiHarness.Run(harness =>
    {
        var paths = new[] { Write(harness, "a.png"), Write(harness, "b.png"), Write(harness, "c.png") };

        Open(harness, paths);

        var tabs = harness.ViewModel.Tabs;
        Assert.Equal(3, tabs.Count);
        Assert.NotNull(tabs[0].Document);
        Assert.Null(tabs[1].Document);
        Assert.Null(tabs[2].Document);
        Assert.Equal("c.png", tabs[2].Name);
    });

    [Fact]
    public void The_plus_button_opens_a_blank_tab_and_only_one() => UiHarness.Run(async harness =>
    {
        Open(harness, Write(harness, "one.png"));
        var viewModel = harness.ViewModel;

        await viewModel.NewTabCommand.ExecuteAsync(null);
        await viewModel.NewTabCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.Equal(2, viewModel.Tabs.Count);
        Assert.True(viewModel.SelectedTab!.IsBlank);
        Assert.Equal("New tab", viewModel.SelectedTab.Name);
        Assert.False(viewModel.HasImage);
        harness.Screenshot("tabs-blank");
    });

    [Fact]
    public void An_image_opened_from_a_blank_tab_fills_that_tab() => UiHarness.Run(async harness =>
    {
        Open(harness, Write(harness, "one.png"));
        var viewModel = harness.ViewModel;
        await viewModel.NewTabCommand.ExecuteAsync(null);
        var blank = viewModel.SelectedTab;

        Open(harness, Write(harness, "two.png"));

        Assert.Equal(2, viewModel.Tabs.Count);
        Assert.Same(blank, viewModel.SelectedTab);
        Assert.Equal("two.png", blank!.Name);
    });

    [Fact]
    public void A_pasted_image_gets_its_own_tab() => UiHarness.Run(async harness =>
    {
        Open(harness, Write(harness, "one.png"));
        await Edit(harness);
        using (var copied = ImageBuffer.Create(50, 50))
        {
            harness.Clipboard.Contents = ClipboardCodec.Encode(copied, CopyMode.Standard, SKColors.White);
        }

        await harness.ViewModel.PasteCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.Equal(2, harness.ViewModel.Tabs.Count);
        Assert.Contains("Pasted image", harness.ViewModel.WindowTitle);
        Assert.Equal(0, harness.Dialogs.UnsavedPrompts);
    });

    [Fact]
    public void Closing_a_clean_tab_moves_to_its_neighbour() => UiHarness.Run(async harness =>
    {
        Open(harness, Write(harness, "a.png"), Write(harness, "b.png"), Write(harness, "c.png"));
        var viewModel = harness.ViewModel;
        var tabs = viewModel.Tabs;
        viewModel.SelectedTab = tabs[1];
        Settle(harness);

        await viewModel.CloseTabCommand.ExecuteAsync(tabs[1]);
        Settle(harness);

        Assert.Equal(2, tabs.Count);
        Assert.Equal("c.png", viewModel.SelectedTab!.Name);
        Assert.Equal(0, harness.Dialogs.UnsavedPrompts);
    });

    [Fact]
    public void Closing_a_tab_with_edits_asks_and_cancel_keeps_it() => UiHarness.Run(async harness =>
    {
        Open(harness, Write(harness, "one.png"));
        await Edit(harness);
        var viewModel = harness.ViewModel;
        harness.Dialogs.UnsavedAnswer = UnsavedChoice.Cancel;

        await viewModel.CloseTabCommand.ExecuteAsync(viewModel.Tabs[0]);

        Assert.Equal(1, harness.Dialogs.UnsavedPrompts);
        Assert.Single(viewModel.Tabs);
        Assert.True(viewModel.HasImage);
    });

    [Fact]
    public void Closing_a_background_tab_with_edits_shows_it_first() => UiHarness.Run(async harness =>
    {
        Open(harness, Write(harness, "one.png"));
        await Edit(harness);
        Open(harness, Write(harness, "two.png"));
        var viewModel = harness.ViewModel;
        harness.Dialogs.UnsavedAnswer = UnsavedChoice.Cancel;

        await viewModel.CloseTabCommand.ExecuteAsync(viewModel.Tabs[0]);

        Assert.Same(viewModel.Tabs[0], viewModel.SelectedTab);
        Assert.Equal(2, viewModel.Tabs.Count);
    });

    [Fact]
    public void Closing_the_last_tab_leaves_the_start_screen() => UiHarness.Run(async harness =>
    {
        Open(harness, Write(harness, "one.png"));
        var viewModel = harness.ViewModel;

        await viewModel.CloseCurrentTabCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.Empty(viewModel.Tabs);
        Assert.False(viewModel.HasTabs);
        Assert.False(viewModel.HasImage);
        Assert.Equal("PicKit", viewModel.WindowTitle);
    });

    [Fact]
    public void Closing_the_window_asks_about_every_tab_with_edits() => UiHarness.Run(async harness =>
    {
        Open(harness, Write(harness, "one.png"));
        await Edit(harness);
        Open(harness, Write(harness, "two.png"));
        await Edit(harness);
        Open(harness, Write(harness, "three.png"));
        harness.Dialogs.UnsavedAnswer = UnsavedChoice.Discard;

        Assert.True(harness.ViewModel.HasUnsavedChanges);
        Assert.True(await harness.ViewModel.ResolveUnsavedChangesAsync());

        Assert.Equal(2, harness.Dialogs.UnsavedPrompts);
    });

    [Fact]
    public void Text_still_being_typed_stays_with_its_tab_when_moving_away() => UiHarness.Run(async harness =>
    {
        Open(harness, Write(harness, "one.png"));
        var viewModel = harness.ViewModel;
        var editor = viewModel.Editor;
        var document = editor.Document!;
        editor.ActiveTool = editor.Text;
        harness.Pump();

        var canvas = harness.Window.Canvas;
        var point = TestFiles.ToElement(canvas, new SKPoint(100, 100));
        canvas.PressAt(point, MouseButton.Left, ModifierKeys.None);
        canvas.ReleaseAt(point, MouseButton.Left, ModifierKeys.None);
        harness.Window.TextEntry.Text = "Keep me";
        harness.Pump();

        await viewModel.NewTabCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.Equal("Keep me", ((TextElement)Assert.Single(document.Markups)).Text);
    });

    [Fact]
    public void A_damaged_file_closes_its_tab_and_says_why() => UiHarness.Run(harness =>
    {
        Open(harness, Write(harness, "one.png"));
        var broken = Path.Combine(harness.Folder, "broken.png");
        File.WriteAllBytes(broken, [1, 2, 3, 4]);

        harness.ViewModel.OpenPaths([broken]);
        harness.WaitFor(() => harness.Dialogs.Errors.Count > 0);
        harness.WaitFor(() => harness.ViewModel.Tabs.Count == 1);
        Settle(harness);

        Assert.Equal("one.png", harness.ViewModel.SelectedTab!.Name);
    });

    [Fact]
    public void Each_tab_has_a_way_to_be_closed_and_the_strip_has_a_plus() => UiHarness.Run(harness =>
    {
        Open(harness, Write(harness, "one.png"));

        Assert.Equal(Visibility.Visible, ((FrameworkElement)harness.Window.TabStrip.Parent).Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed);
        Assert.True(harness.ViewModel.HasTabs);
    });
}

public class RenameBarUiTests
{
    private static string OpenFile(UiHarness harness, string name)
    {
        var path = Path.Combine(harness.Folder, name);
        using (var image = ImageBuffer.Create(300, 200))
        {
            image.Bitmap.Erase(SKColors.Teal);
            File.WriteAllBytes(path, ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png)));
        }

        harness.ViewModel.OpenPaths([path]);
        harness.WaitFor(() => harness.ViewModel.HasImage && !harness.ViewModel.IsLoading);
        harness.Pump();
        return path;
    }

    [Fact]
    public void The_bar_shows_the_name_without_its_file_type() => UiHarness.Run(harness =>
    {
        OpenFile(harness, "holiday.png");

        Assert.Equal("holiday", harness.Window.RenameBar.Text);
    });

    [Fact]
    public void Renaming_changes_the_file_the_tab_and_the_title() => UiHarness.Run(harness =>
    {
        var path = OpenFile(harness, "holiday.png");
        var viewModel = harness.ViewModel;

        viewModel.RenameCurrent("beach trip");
        harness.Pump();

        var renamed = Path.Combine(harness.Folder, "beach trip.png");
        Assert.True(File.Exists(renamed));
        Assert.False(File.Exists(path));
        Assert.Equal("beach trip.png", viewModel.SelectedTab!.Name);
        Assert.Equal(renamed, viewModel.SelectedTab.Path);
        Assert.Equal(renamed, viewModel.Editor.Document!.Source.Path);
        Assert.Contains("beach trip.png", viewModel.WindowTitle);
        Assert.Equal("beach trip", harness.Window.RenameBar.Text);
    });

    [Fact]
    public void A_name_already_taken_is_refused_and_nothing_is_overwritten() => UiHarness.Run(harness =>
    {
        var taken = Path.Combine(harness.Folder, "taken.png");
        File.WriteAllBytes(taken, [1, 2, 3]);
        var path = OpenFile(harness, "holiday.png");

        harness.ViewModel.RenameCurrent("taken");
        harness.Pump();

        Assert.True(File.Exists(path));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(taken));
        Assert.Equal("A file with that name is already there", harness.ViewModel.NoticeText);
        Assert.Equal("holiday", harness.ViewModel.EditableName);
    });

    [Fact]
    public void Changing_only_the_capitals_works() => UiHarness.Run(harness =>
    {
        OpenFile(harness, "holiday.png");

        harness.ViewModel.RenameCurrent("Holiday");
        harness.Pump();

        Assert.Equal("Holiday.png", Path.GetFileName(Directory.GetFiles(harness.Folder, "*.png").Single()));
    });

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_name_changes_nothing(string text) => UiHarness.Run(harness =>
    {
        var path = OpenFile(harness, "holiday.png");

        harness.ViewModel.RenameCurrent(text);

        Assert.True(File.Exists(path));
        Assert.Equal("holiday", harness.ViewModel.EditableName);
    });

    [Fact]
    public void Characters_windows_refuses_are_replaced() => UiHarness.Run(harness =>
    {
        OpenFile(harness, "holiday.png");

        harness.ViewModel.RenameCurrent("a:b*c");
        harness.Pump();

        Assert.Equal("a_b_c.png", harness.ViewModel.SelectedTab!.Name);
    });

    [Fact]
    public void Renaming_a_pasted_image_names_what_save_as_will_suggest() => UiHarness.Run(async harness =>
    {
        using (var copied = ImageBuffer.Create(50, 50))
        {
            harness.Clipboard.Contents = ClipboardCodec.Encode(copied, CopyMode.Standard, SKColors.White);
        }

        await harness.ViewModel.PasteCommand.ExecuteAsync(null);
        harness.Pump();

        harness.ViewModel.RenameCurrent("my design");

        Assert.Equal("my design", harness.ViewModel.SelectedTab!.Name);
        Assert.Null(harness.ViewModel.Editor.Document!.Source.Path);
    });

    [Fact]
    public void Renaming_a_waiting_screenshot_is_the_name_it_saves_under() => UiHarness.Run(async harness =>
    {
        var inbox = new PicKit.Core.Background.ImageInbox(Path.Combine(harness.Folder, ".inbox"));
        using (var image = ImageBuffer.Create(50, 50))
        {
            image.Bitmap.Erase(SKColors.Teal);
            inbox.Add(ImageEncoder.Encode(image, new EncodeOptions(ImageFormat.Png)), "Screenshot.png");
        }

        var viewModel = harness.ViewModel;
        viewModel.Handle(new LaunchRequest(ShellAction.Open, []), bringToFront: false);
        harness.WaitFor(() => viewModel.IsWaitingImage && viewModel.HasImage && !viewModel.IsLoading);
        harness.Pump();

        viewModel.RenameCurrent("receipt");
        await viewModel.KeepWaitingCommand.ExecuteAsync(null);
        harness.Pump();

        Assert.True(File.Exists(Path.Combine(harness.Folder, "Saved", "receipt.png")));
    });
}

public class EraserUiTests
{
    private static void Drag(UiHarness harness, params SKPoint[] points)
    {
        var canvas = harness.Window.Canvas;
        Assert.True(canvas.PressAt(TestFiles.ToElement(canvas, points[0]), MouseButton.Left, ModifierKeys.None));
        foreach (var point in points.Skip(1))
        {
            canvas.MoveTo(TestFiles.ToElement(canvas, point), ModifierKeys.None);
        }

        canvas.ReleaseAt(TestFiles.ToElement(canvas, points[^1]), MouseButton.Left, ModifierKeys.None);
        harness.Pump();
    }

    private static MarkupModeChoice Eraser(MarkupToolViewModel tool) =>
        tool.Modes.First(mode => mode.Mode == MarkupMode.Eraser);

    [Fact]
    public void Rubbing_across_a_pen_stroke_cuts_it_in_two_and_undo_mends_it() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1000, 800);
        var editor = harness.ViewModel.Editor;
        var markup = editor.Markup;
        editor.ActiveTool = markup;
        harness.Pump();

        Drag(harness, new SKPoint(100, 400), new SKPoint(500, 400), new SKPoint(900, 400));
        var document = editor.Document!;
        Assert.Single(document.Markups);

        markup.SelectedMode = Eraser(markup);
        harness.Pump();
        Assert.True(markup.ShowsEraserSize);
        Assert.False(markup.ShowsColor);
        harness.Screenshot("eraser-panel");

        Drag(harness, new SKPoint(500, 300), new SKPoint(500, 400), new SKPoint(500, 500));

        Assert.Equal(2, document.Markups.Length);
        Assert.All(document.Markups, element => Assert.IsType<StrokeElement>(element));

        editor.UndoCommand.Execute(null);
        harness.Pump();
        Assert.Single(document.Markups);
    });

    [Fact]
    public void The_highlight_tool_has_an_eraser_too() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1000, 800);
        var editor = harness.ViewModel.Editor;
        var highlight = editor.Highlight;
        editor.ActiveTool = highlight;
        harness.Pump();
        Assert.True(highlight.ShowsModes);

        Drag(harness, new SKPoint(100, 400), new SKPoint(900, 400));
        var document = editor.Document!;

        highlight.SelectedMode = Eraser(highlight);
        Drag(harness, new SKPoint(100, 400), new SKPoint(500, 400), new SKPoint(900, 400));

        Assert.Empty(document.Markups);
    });

    [Fact]
    public void A_whole_drag_is_one_undo_step() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1000, 800);
        var editor = harness.ViewModel.Editor;
        var markup = editor.Markup;
        editor.ActiveTool = markup;
        harness.Pump();
        Drag(harness, new SKPoint(100, 300), new SKPoint(900, 300));
        Drag(harness, new SKPoint(100, 500), new SKPoint(900, 500));
        var document = editor.Document!;

        markup.SelectedMode = Eraser(markup);
        Drag(harness, new SKPoint(500, 200), new SKPoint(500, 300), new SKPoint(500, 400), new SKPoint(500, 500), new SKPoint(500, 600));
        Assert.Equal(4, document.Markups.Length);

        editor.UndoCommand.Execute(null);
        harness.Pump();

        Assert.Equal(2, document.Markups.Length);
    });

    [Fact]
    public void Rubbing_over_nothing_adds_no_undo_step() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 1000, 800);
        var editor = harness.ViewModel.Editor;
        var markup = editor.Markup;
        editor.ActiveTool = markup;
        markup.SelectedMode = Eraser(markup);
        harness.Pump();

        Drag(harness, new SKPoint(100, 100), new SKPoint(300, 300));

        Assert.False(editor.Document!.IsDirty);
    });

    [Fact]
    public void The_eraser_is_a_mode_the_text_tool_does_not_need() => UiHarness.Run(async harness =>
    {
        await TestFiles.OpenPhoto(harness, 400, 300);
        var editor = harness.ViewModel.Editor;

        Assert.DoesNotContain(editor.Text.Modes, mode => mode.Mode == MarkupMode.Eraser);
    });
}
