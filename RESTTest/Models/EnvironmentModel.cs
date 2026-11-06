namespace RESTTest.Models;

/// <summary>Persisted form of a named base URL belonging to a collection.</summary>
public sealed class EnvironmentModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "New URL";

    public string BaseUrl { get; set; } = string.Empty;
}
