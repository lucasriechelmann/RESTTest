using System.ComponentModel;
using RESTTest.ViewModels;

namespace RESTTest.Views;

/// <summary>
/// The shell. All behaviour lives in <see cref="MainViewModel"/>; the only code here is the
/// flush on close, which is a window-lifetime concern rather than a ViewModel one.
/// </summary>
public partial class MainWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;

        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Auto-save is debounced, so an edit made in the last few hundred milliseconds
        // would otherwise be lost on exit.
        _viewModel.FlushPendingSaves();
        _viewModel.SaveUiState();

        base.OnClosing(e);
    }
}
