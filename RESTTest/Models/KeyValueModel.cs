namespace RESTTest.Models;

/// <summary>Persisted form of a single query parameter or header row.</summary>
public sealed class KeyValueModel
{
    public bool Enabled { get; set; } = true;

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// When set, the value is taken from the collection's captured value of this name at send time
    /// and <see cref="Value"/> is ignored. Null means the typed value is used. An empty string means
    /// "from a capture" with no name picked yet, which fails the send with a message.
    /// </summary>
    public string? Capture { get; set; }
}
