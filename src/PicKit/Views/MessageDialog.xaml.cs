using System.Windows;

namespace PicKit.Views;

public enum MessageDialogOutcome
{
    Closed,
    Confirmed,
    Alternate,
}

public partial class MessageDialog : Window
{
    public MessageDialog(
        string heading,
        string message,
        string? details,
        string closeLabel,
        string? confirmLabel = null,
        string? alternateLabel = null)
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

        if (alternateLabel is not null)
        {
            AlternateButton.Content = alternateLabel;
            AlternateButton.Visibility = Visibility.Visible;
        }
    }

    public MessageDialogOutcome Outcome { get; private set; }

    private void OnConfirm(object sender, RoutedEventArgs e) => Finish(MessageDialogOutcome.Confirmed);

    private void OnAlternate(object sender, RoutedEventArgs e) => Finish(MessageDialogOutcome.Alternate);

    private void OnClose(object sender, RoutedEventArgs e) => Finish(MessageDialogOutcome.Closed);

    private void Finish(MessageDialogOutcome outcome)
    {
        Outcome = outcome;
        Close();
    }
}
