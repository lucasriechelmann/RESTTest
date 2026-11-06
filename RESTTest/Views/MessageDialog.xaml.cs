using System.Windows;

namespace RESTTest.Views;

/// <summary>
/// Shared shell for confirmations and notices. Code-behind only: a dialog whose entire job is to
/// return a boolean has no state worth a ViewModel, and the callers go through IDialogService.
/// </summary>
public partial class MessageDialog
{
    public MessageDialog(string title, string message, string? acceptText, bool showCancel)
    {
        InitializeComponent();

        Title = title;
        MessageText.Text = message;
        AcceptButton.Content = acceptText ?? "OK";
        CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAccept(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
