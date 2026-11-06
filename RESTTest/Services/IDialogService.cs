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

    /// <summary>Puts text on the clipboard, swallowing the transient failures the clipboard throws.</summary>
    void CopyToClipboard(string text);
}
