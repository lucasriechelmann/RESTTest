namespace RESTTest.Models;

/// <summary>
/// One value a capture stored, kept in the collection file so a login survives a restart. Values
/// are plain text, like tokens typed on the Auth tab.
/// </summary>
public sealed class CapturedValueModel
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    /// <summary>The name of the request whose response the value came from, for display.</summary>
    public string Source { get; set; } = string.Empty;

    public DateTimeOffset CapturedAt { get; set; }
}
