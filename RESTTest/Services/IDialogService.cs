using RESTTest.ViewModels;

namespace RESTTest.Services;

/// <summary>
/// The seam that keeps ViewModels free of window references. Implemented in the view layer.
/// </summary>
public interface IDialogService
{
    /// <summary>Returns true when the user accepts. Used for destructive actions.</summary>
    bool Confirm(string title, string message, string acceptText = "OK");

    /// <summary>Single-line text prompt. Returns null when cancelled.</summary>
    string? Prompt(string title, string label, string initialValue = "", string placeholder = "");

    void ShowMessage(string title, string message);

    /// <summary>
    /// Opens the base-URL editor for a collection. Edits are applied to the live ViewModel, so
    /// auto-save picks them up as they are typed.
    /// </summary>
    void ShowEnvironments(CollectionViewModel collection);

    /// <summary>
    /// Shows the run report for a runner that has already been started. Blocks until the dialog is
    /// closed, and cancels the run if it is still going at that point.
    /// </summary>
    void ShowRunner(RunnerViewModel runner);

    /// <summary>Puts text on the clipboard, swallowing the transient failures the clipboard throws.</summary>
    void CopyToClipboard(string text);
}
