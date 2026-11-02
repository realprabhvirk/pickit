using System.Windows;
using System.Windows.Media;
using PicKit.Services;
using SkiaSharp;

namespace PicKit.Views;

public partial class TransparencyDialog : Window
{
    private readonly ColorService _colors;
    private Color _custom;

    public TransparencyDialog(SKColor initial, ColorService colors)
    {
        InitializeComponent();
        _colors = colors;
        _custom = Color.FromRgb(initial.Red, initial.Green, initial.Blue);
        CustomSwatch.Background = new SolidColorBrush(_custom);

        if (initial == SKColors.Black)
        {
            BlackOption.IsChecked = true;
        }
        else if (initial == SKColors.White)
        {
            WhiteOption.IsChecked = true;
        }
        else
        {
            CustomOption.IsChecked = true;
        }
    }

    public SKColor SelectedColor { get; private set; } = SKColors.White;

    private void OnChooseColor(object sender, RoutedEventArgs e)
    {
        if (_colors.Pick(_custom) is { } picked)
        {
            _custom = picked;
            CustomSwatch.Background = new SolidColorBrush(picked);
            CustomOption.IsChecked = true;
        }
    }

    private void OnContinue(object sender, RoutedEventArgs e)
    {
        SelectedColor = BlackOption.IsChecked == true
            ? SKColors.Black
            : CustomOption.IsChecked == true ? new SKColor(_custom.R, _custom.G, _custom.B) : SKColors.White;

        DialogResult = true;
    }
}
