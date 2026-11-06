using System.Windows;
using RESTTest.Services;
using RESTTest.ViewModels;
using RESTTest.Views;
using Wpf.Ui.Appearance;

namespace RESTTest;

/// <summary>
/// Composition root. Everything is wired by hand here: with four services there is nothing a
/// DI container would buy, and keeping it explicit makes the dependency graph readable.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Light or dark, following the Windows setting rather than forcing one.
        ApplicationThemeManager.ApplySystemTheme();

        var store = new CollectionStore();
        var cookieStore = new CookieStore(store.WorkspacePath);
        var cookieJar = new CookieJar();
        cookieJar.Load(cookieStore.Load());

        var sender = new HttpRequestSender(cookieJar);
        var dialogs = new DialogService();
        var historyStore = new HistoryStore(store.WorkspacePath);

        var viewModel = new MainViewModel(store, sender, dialogs, historyStore, cookieJar, cookieStore);
        var window = new MainWindow(viewModel);

        dialogs.Owner = window;
        MainWindow = window;

        window.Show();
    }
}
