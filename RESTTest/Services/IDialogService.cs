using RESTTest.ViewModels;

namespace RESTTest.Services;

/// <summary>
/// The seam that keeps ViewModels free of window references. Implemented in the view layer.
/// </summary>
public interface IDialogService
{
    /// <summary>Returns true when the user accepts. Used for destructive actions.</summary>
    bool Confirm(string title, string message, string acceptText = "OK");

    /// <summary>
    /// Text prompt. Returns null when cancelled. <paramref name="multiline"/> gives a larger box
    /// that accepts newlines, for pasting something like a cURL command.
    /// </summary>
    string? Prompt(string title, string label, string initialValue = "", string placeholder = "", bool multiline = false);

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

    /// <summary>
    /// Lets the user choose a file. Returns its full path, or null when cancelled.
    /// <paramref name="filter"/> is a Windows file-dialog filter ("Name|*.ext|All|*.*"); null shows every file.
    /// </summary>
    string? PickFile(string title, string? filter = null);

    /// <summary>
    /// Asks where to save a file, proposing <paramref name="suggestedName"/>. Returns the chosen full
    /// path, or null when cancelled. Overwriting an existing file is confirmed by the dialog itself.
    /// </summary>
    string? PickSavePath(string title, string suggestedName, string? filter = null);

    /// <summary>Opens the cookie jar. Blocks until the dialog is closed.</summary>
    void ShowCookies(CookiesViewModel cookies);

    /// <summary>Opens the request history. Blocks until the dialog is closed.</summary>
    void ShowHistory(HistoryViewModel history);

    /// <summary>Puts text on the clipboard, swallowing the transient failures the clipboard throws.</summary>
    void CopyToClipboard(string text);
}
