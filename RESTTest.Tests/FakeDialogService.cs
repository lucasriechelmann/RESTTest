using RESTTest.Services;
using RESTTest.ViewModels;

namespace RESTTest.Tests;

/// <summary>Hand-written <see cref="IDialogService"/> for ViewModel tests: no windows, answers are set by the test.</summary>
internal sealed class FakeDialogService : IDialogService
{
    /// <summary>What <see cref="Confirm"/> returns.</summary>
    public bool ConfirmResult { get; set; } = true;

    public List<string> ConfirmMessages { get; } = [];

    public bool Confirm(string title, string message, string acceptText = "OK")
    {
        ConfirmMessages.Add(message);
        return ConfirmResult;
    }

    public string? Prompt(string title, string label, string initialValue = "", string placeholder = "") => null;

    public void ShowMessage(string title, string message)
    {
    }

    /// <summary>What <see cref="PickFile"/> returns; null means the user cancelled.</summary>
    public string? PickFileResult { get; set; }

    public string? PickFile(string title) => PickFileResult;

    public void ShowEnvironments(CollectionViewModel collection)
    {
    }

    public void ShowRunner(RunnerViewModel runner)
    {
    }

    public void ShowHistory(HistoryViewModel history)
    {
    }

    public void CopyToClipboard(string text)
    {
    }
}
