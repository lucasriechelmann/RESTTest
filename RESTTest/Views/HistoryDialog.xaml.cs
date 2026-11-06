using System.Windows;
using RESTTest.ViewModels;

namespace RESTTest.Views;

/// <summary>
/// Browser for the request history. State and commands live on the <see cref="HistoryViewModel"/>;
/// the only code here is closing the window.
/// </summary>
public partial class HistoryDialog
{
    public HistoryDialog(HistoryViewModel history)
    {
        InitializeComponent();
        DataContext = history;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
