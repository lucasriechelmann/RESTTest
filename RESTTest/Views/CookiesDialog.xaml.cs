using System.Windows;
using RESTTest.ViewModels;

namespace RESTTest.Views;

/// <summary>The cookie jar window. State and commands live on <see cref="CookiesViewModel"/>; this only closes the window.</summary>
public partial class CookiesDialog
{
    public CookiesDialog(CookiesViewModel cookies)
    {
        InitializeComponent();
        DataContext = cookies;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
