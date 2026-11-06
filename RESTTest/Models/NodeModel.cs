namespace RESTTest.Models;

/// <summary>
/// Persisted form of one sidebar entry. A single recursive type covers both folders and
/// requests so that reordering, renaming and drag-and-drop are uniform operations;
/// <see cref="Request"/> is populated only when <see cref="Kind"/> is <see cref="NodeKind.Request"/>.
/// </summary>
public sealed class NodeModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public NodeKind Kind { get; set; } = NodeKind.Request;

    public string Name { get; set; } = "New Request";

    /// <summary>Folder expansion state, persisted so the tree looks the same on next launch.</summary>
    public bool IsExpanded { get; set; }

    public List<NodeModel> Children { get; set; } = [];

    public RequestModel? Request { get; set; }
}
