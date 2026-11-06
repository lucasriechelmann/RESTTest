namespace RESTTest.Models;

/// <summary>Small bag of cross-session UI state, stored next to the collections.</summary>
public sealed class AppSettings
{
    public string? LastCollectionId { get; set; }

    public string? LastSelectedNodeId { get; set; }
}
