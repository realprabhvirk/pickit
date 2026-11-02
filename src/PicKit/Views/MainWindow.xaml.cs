using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PicKit.Core.Settings;
using PicKit.Core.Shell;
using PicKit.ViewModels;
using PicKit.ViewModels.Tools;

namespace PicKit.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly SettingsStore _settings;
    private MarkupToolViewModel? _textTool;
    private bool _closeConfirmed;

    public MainWindow(MainViewModel viewModel, SettingsStore settings)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _settings = settings;
        RestorePlacement();
        BindCanvasShortcuts();
        Drop += OnDrop;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        viewModel.Editor.RedrawRequested += Canvas.Redraw;
        viewModel.Editor.Markup.PropertyChanged += OnMarkupToolChanged;
        viewModel.Editor.Text.PropertyChanged += OnMarkupToolChanged;
        Deactivated += (_, _) => Canvas.IsPanModifierHeld = false;
        Activated += (_, _) => viewModel.RefreshClipboardState();
        viewModel.ActivationRequested += BringToFront;
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || !_viewModel.HasUnsavedChanges)
        {
            SavePlacement();
            return;
        }

        // Closing has to wait for the answer, so hold it here and close again if it's safe.
        e.Cancel = true;
        await System.Windows.Threading.Dispatcher.Yield();
        if (await _viewModel.ResolveUnsavedChangesAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private void BindCanvasShortcuts()
    {
        void Bind(ICommand command, params Key[] keys)
        {
            foreach (var key in keys)
            {
                InputBindings.Add(new KeyBinding(command, key, ModifierKeys.Control));
            }
        }

        Bind(Canvas.ZoomInCommand, Key.OemPlus, Key.Add);
        Bind(Canvas.ZoomOutCommand, Key.OemMinus, Key.Subtract);
        Bind(Canvas.FitCommand, Key.D0, Key.NumPad0);
        Bind(Canvas.ActualSizeCommand, Key.D1, Key.NumPad1);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Space belongs to whatever button or text box has focus; it only pans over the canvas.
        if (e.Key == Key.Space && Keyboard.FocusedElement is not (System.Windows.Controls.TextBox or ButtonBase))
        {
            Canvas.IsPanModifierHeld = true;
            e.Handled = true;
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            Canvas.IsPanModifierHeld = false;
        }
    }

    private void BringToFront()
    {
        // Tests run an off-screen window that must never take focus from the desktop.
        if (!ShowActivated)
        {
            return;
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void OnDropDownMenu(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.DataContext = DataContext;
            menu.IsOpen = true;
        }
    }

    private void OnMarkupToolChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MarkupToolViewModel.TextEdit) && sender is MarkupToolViewModel tool)
        {
            _textTool = tool;
            ShowTextEntry(tool);
        }
    }

    /// <summary>Places a real text box exactly where the finished text will be drawn.</summary>
    private void ShowTextEntry(MarkupToolViewModel tool)
    {
        if (tool.TextEdit is not { } session)
        {
            TextEntry.Visibility = Visibility.Collapsed;
            return;
        }

        var view = Canvas.View;
        var scale = view.DeviceScale;
        var position = view.ImageToView(session.Position);
        var text = session.Existing;

        System.Windows.Controls.Canvas.SetLeft(TextEntry, position.X / scale);
        System.Windows.Controls.Canvas.SetTop(TextEntry, position.Y / scale);

        TextEntry.FontSize = (text?.FontSize ?? tool.FontSizeInImagePixels) * view.Zoom / scale;
        TextEntry.Foreground = new SolidColorBrush(tool.Color);
        TextEntry.CaretBrush = TextEntry.Foreground;
        TextEntry.Text = text?.Text ?? string.Empty;
        TextEntry.Visibility = Visibility.Visible;
        TextEntry.Focus();
        TextEntry.SelectAll();
    }

    private void FinishTextEntry(bool cancel)
    {
        var tool = _textTool;
        if (tool?.TextEdit is null)
        {
            return;
        }

        var text = TextEntry.Text;
        TextEntry.Visibility = Visibility.Collapsed;
        tool.CommitTextEdit(text, cancel);
        Canvas.Focus();
    }

    private void OnTextEntryKeyDown(object sender, KeyEventArgs e)
    {
        // Enter makes a new line, as in any text box; Ctrl+Enter finishes, Escape abandons.
        if (e.Key == Key.Escape)
        {
            FinishTextEntry(cancel: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            FinishTextEntry(cancel: false);
            e.Handled = true;
        }
    }

    private void OnTextEntryLostFocus(object sender, KeyboardFocusChangedEventArgs e) => FinishTextEntry(cancel: false);

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _viewModel.Handle(new LaunchRequest(ShellAction.Open, paths));
        }
    }

    private void RestorePlacement()
    {
        var saved = _settings.Current.Window;
        if (!_settings.Current.RememberWindow || saved is null || saved.Width <= 0 || saved.Height <= 0)
        {
            return;
        }

        // A saved position may belong to a monitor that is no longer attached.
        var visible = saved.Left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80
            && saved.Top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80
            && saved.Left + saved.Width > SystemParameters.VirtualScreenLeft + 80
            && saved.Top >= SystemParameters.VirtualScreenTop;

        Width = saved.Width;
        Height = saved.Height;
        if (visible)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = saved.Left;
            Top = saved.Top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (saved.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void SavePlacement()
    {
        if (!_settings.Current.RememberWindow)
        {
            return;
        }

        var bounds = RestoreBounds;
        _settings.Current.Window = new WindowPlacementSettings
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            IsMaximized = WindowState == WindowState.Maximized,
        };
    }
}
