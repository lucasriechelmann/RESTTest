namespace RESTTest.Models;

/// <summary>Persisted form of a single query parameter or header row.</summary>
public sealed class KeyValueModel
{
    public bool Enabled { get; set; } = true;

    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
