using System.Windows;

namespace PicKit.Views;

public partial class MessageDialog : Window
{
    public MessageDialog(string heading, string message, string? details, string closeLabel, string? confirmLabel = null)
    {
        InitializeComponent();

        HeadingText.Text = heading;
        BodyText.Text = message;
        CloseButton.Content = closeLabel;

        if (details is not null)
        {
            DetailsText.Text = details;
            DetailsExpander.Visibility = Visibility.Visible;
        }

        if (confirmLabel is not null)
        {
            ConfirmButton.Content = confirmLabel;
            ConfirmButton.Visibility = Visibility.Visible;
        }
        else
        {
            CloseButton.IsDefault = true;
        }
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnClose(object sender, RoutedEventArgs e) => DialogResult = false;
}
