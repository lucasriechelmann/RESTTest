using System.Windows;
using RESTTest.ViewModels;

namespace RESTTest.Views;

/// <summary>
/// Editor for the collection's base URLs. It binds straight to the live
/// <see cref="CollectionViewModel"/>, so edits are picked up by auto-save as they are typed and
/// there is no apply/cancel state to manage.
/// </summary>
public partial class EnvironmentsDialog
{
    public EnvironmentsDialog(CollectionViewModel collection)
    {
        InitializeComponent();
        DataContext = collection;
    }

    private void OnDone(object sender, RoutedEventArgs e) => Close();
}
