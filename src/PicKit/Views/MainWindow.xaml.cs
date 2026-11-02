using System.ComponentModel;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using PicKit.Core.Settings;
using PicKit.ViewModels;

namespace PicKit.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly SettingsStore _settings;

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
        Deactivated += (_, _) => Canvas.IsPanModifierHeld = false;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        SavePlacement();
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

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            _viewModel.OpenPaths(paths);
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
