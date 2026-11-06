using RESTTest.Models;

namespace RESTTest.Services.Importing;

/// <summary>
/// What an importer produced: a whole collection ready to be added, plus everything the user
/// should be told about because it did not survive the trip.
/// </summary>
public sealed class ImportedCollection
{
    /// <summary>"Postman", "Insomnia" or "OpenAPI": shown in the summary.</summary>
    public required string Format { get; init; }

    public string Name { get; set; } = "Imported collection";

    /// <summary>The collection's base URLs. The first one is the one requests were shortened against.</summary>
    public List<EnvironmentModel> Environments { get; } = [];

    public List<NodeModel> Items { get; set; } = [];

    /// <summary>Plain sentences, one per kind of loss (scripts, unsupported auth, left-over placeholders...).</summary>
    public List<string> Warnings { get; } = [];

    public int RequestCount => Count(Items, NodeKind.Request);

    public int FolderCount => Count(Items, NodeKind.Folder);

    public CollectionModel ToModel() => new()
    {
        Name = Name,
        Environments = Environments,
        ActiveEnvironmentId = Environments.FirstOrDefault()?.Id,
        Items = Items,
    };

    private static int Count(IEnumerable<NodeModel> nodes, NodeKind kind) =>
        nodes.Sum(n => (n.Kind == kind ? 1 : 0) + Count(n.Children, kind));
}
