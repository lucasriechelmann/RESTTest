namespace RESTTest.Models;

/// <summary>
/// Persisted form of a collection: the environment list shared by every request it contains,
/// plus the folder/request tree. One of these is written per file under the workspace folder.
/// </summary>
public sealed class CollectionModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "New Collection";

    /// <summary>The selectable base URLs. Collection-wide, by design.</summary>
    public List<EnvironmentModel> Environments { get; set; } = [];

    public string? ActiveEnvironmentId { get; set; }

    public List<NodeModel> Items { get; set; } = [];
}
