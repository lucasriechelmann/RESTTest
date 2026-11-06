using System.Windows;
using RESTTest.Services;
using RESTTest.ViewModels;

namespace RESTTest.Views;

/// <summary>
/// View-layer implementation of <see cref="IDialogService"/>. Lives here, next to the windows it
/// opens, so that nothing in the ViewModel layer ever references a <see cref="Window"/>.
/// </summary>
public sealed class DialogService : IDialogService
{
    /// <summary>Set once by the composition root; used to centre dialogs and keep them modal.</summary>
    public Window? Owner { get; set; }

    public bool Confirm(string title, string message, string acceptText = "OK")
    {
        var dialog = new MessageDialog(title, message, acceptText, showCancel: true) { Owner = Owner };
        return dialog.ShowDialog() == true;
    }

    public string? Prompt(string title, string label, string initialValue = "", string placeholder = "", bool multiline = false)
    {
        var dialog = new PromptDialog(title, label, initialValue, placeholder, multiline) { Owner = Owner };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    public void ShowMessage(string title, string message)
    {
        var dialog = new MessageDialog(title, message, "OK", showCancel: false) { Owner = Owner };
        dialog.ShowDialog();
    }

    public void ShowEnvironments(CollectionViewModel collection)
    {
        var dialog = new EnvironmentsDialog(collection) { Owner = Owner };
        dialog.ShowDialog();
    }

    public string? PickFile(string title, string? filter = null)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, CheckFileExists = true };
        if (filter is not null)
        {
            dialog.Filter = filter;
        }

        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? PickSavePath(string title, string suggestedName, string? filter = null)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = title, FileName = suggestedName, OverwritePrompt = true };
        if (filter is not null)
        {
            dialog.Filter = filter;
        }

        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public void ShowHistory(HistoryViewModel history)
    {
        var dialog = new HistoryDialog(history) { Owner = Owner };
        dialog.ShowDialog();
    }

    public void ShowRunner(RunnerViewModel runner)
    {
        var dialog = new RunnerDialog(runner) { Owner = Owner };
        dialog.ShowDialog();

        // Closing the window mid-run must not leave requests firing in the background.
        runner.CancelCommand.Execute(null);
    }

    public void CopyToClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception)
        {
            // The clipboard is a shared OS resource and can be locked by another process;
            // a failed copy is not worth interrupting the user over.
        }
    }
}
