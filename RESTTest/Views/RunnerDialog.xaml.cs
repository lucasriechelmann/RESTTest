using System.Windows;
using RESTTest.ViewModels;

namespace RESTTest.Views;

/// <summary>
/// Report window for a collection or folder run. All state and commands live on the
/// <see cref="RunnerViewModel"/>; the only code here is closing the window.
/// </summary>
public partial class RunnerDialog
{
    public RunnerDialog(RunnerViewModel runner)
    {
        InitializeComponent();
        DataContext = runner;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
