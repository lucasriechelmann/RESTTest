using System.IO;
using RESTTest.Models;
using RESTTest.Services;
using RESTTest.ViewModels;

namespace RESTTest.Tests;

/// <summary>
/// Round-trips collections through the JSON files. Uses a temporary workspace so the real
/// %APPDATA%\RestTest folder is never touched.
/// </summary>
public sealed class CollectionStoreTests : IDisposable
{
    private readonly string _workspace;
    private readonly CollectionStore _store;

    public CollectionStoreTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "RestTestTests", Guid.NewGuid().ToString("N"));
        _store = new CollectionStore(_workspace);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workspace))
            {
                Directory.Delete(_workspace, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked temp file must not fail the test run.
        }
    }

    private static CollectionViewModel BuildCollection()
    {
        var collection = new CollectionViewModel { Name = "Sample" };

        var local = new EnvironmentViewModel { Name = "Local", BaseUrl = "http://localhost:5000" };
        var live = new EnvironmentViewModel { Name = "Live", BaseUrl = "https://api.example.com" };
        collection.Environments.Add(local);
        collection.Environments.Add(live);
        collection.ActiveEnvironment = live;

        var request = NodeViewModel.CreateRequest("Create thing");
        request.Request!.Method = "POST";
        request.Request.Path = "/things";
        request.Request.Body = "{\n  \"name\": \"a \\\"quoted\\\" value\"\n}";
        request.Request.Auth = AuthKind.BearerToken;
        request.Request.BearerToken = "token-123";
        request.Request.Headers.Add(new KeyValueViewModel { Key = "X-One", Value = "1" });
        request.Request.QueryParams.Add(new KeyValueViewModel { Key = "q", Value = "x", Enabled = false });

        var inner = NodeViewModel.CreateFolder("Inner");
        inner.Children.Add(request);
        inner.IsExpanded = true;

        var outer = NodeViewModel.CreateFolder("Outer");
        outer.Children.Add(inner);

        collection.Items.Add(outer);
        return collection;
    }

    [Fact]
    public void Saves_and_reloads_a_nested_collection_intact()
    {
        var original = BuildCollection();

        _store.Save(original.ToModel());
        var loaded = _store.LoadAll().Select(CollectionViewModel.FromModel).Single();

        Assert.Equal("Sample", loaded.Name);
        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(2, loaded.Environments.Count);
        Assert.Equal("Live", loaded.ActiveEnvironment?.Name);
        Assert.Equal("https://api.example.com", loaded.ActiveBaseUrl);

        var request = loaded.AllNodes().Single(n => n.IsRequest);
        Assert.Equal("Create thing", request.Name);
        Assert.Equal("POST", request.Request!.Method);
        Assert.Equal("/things", request.Request.Path);
        Assert.Equal(AuthKind.BearerToken, request.Request.Auth);
        Assert.Equal("token-123", request.Request.BearerToken);
        Assert.Equal("X-One", request.Request.Headers.Single().Key);
        Assert.False(request.Request.QueryParams.Single().Enabled);

        // Folder nesting and expansion state survive the round trip.
        Assert.Equal(["Outer", "Inner", "Create thing"], loaded.AllNodes().Select(n => n.Name));
        Assert.True(loaded.AllNodes().Single(n => n.Name == "Inner").IsExpanded);
    }

    [Fact]
    public void Writes_quotes_in_bodies_readably_rather_than_as_unicode_escapes()
    {
        _store.Save(BuildCollection().ToModel());

        var json = File.ReadAllText(Directory.EnumerateFiles(
            Path.Combine(_workspace, "collections"), "*.json").Single());

        Assert.DoesNotContain("\\u0022", json);
        Assert.Contains("\"Method\": \"POST\"", json);
        // Enums are written as names so the files stay hand-editable.
        Assert.Contains("\"Auth\": \"BearerToken\"", json);
    }

    [Fact]
    public void Saving_twice_overwrites_rather_than_duplicating()
    {
        var collection = BuildCollection();
        _store.Save(collection.ToModel());

        collection.Name = "Renamed";
        _store.Save(collection.ToModel());

        var loaded = _store.LoadAll();
        Assert.Single(loaded);
        Assert.Equal("Renamed", loaded[0].Name);
        // Keyed on id, so a rename must not orphan the previous file.
        Assert.Single(Directory.EnumerateFiles(Path.Combine(_workspace, "collections"), "*.json"));
    }

    [Fact]
    public void Delete_removes_the_file()
    {
        var collection = BuildCollection();
        _store.Save(collection.ToModel());

        _store.Delete(collection.Id);

        Assert.Empty(_store.LoadAll());
    }

    [Fact]
    public void Deleting_something_that_is_not_there_is_harmless() =>
        _store.Delete("missing-id");

    [Fact]
    public void An_unreadable_file_is_skipped_rather_than_failing_the_whole_load()
    {
        _store.Save(BuildCollection().ToModel());
        File.WriteAllText(Path.Combine(_workspace, "collections", "broken.json"), "{ not json");

        // One corrupt file must not cost the user every other collection.
        Assert.Single(_store.LoadAll());
    }

    [Fact]
    public void Hand_written_files_without_ids_are_given_them_on_load()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "collections"));
        File.WriteAllText(
            Path.Combine(_workspace, "collections", "manual.json"),
            """
            {
              "Name": "Hand written",
              "Environments": [ { "Name": "Local", "BaseUrl": "http://localhost" } ],
              "Items": [ { "Kind": "Request", "Name": "Ping" } ]
            }
            """);

        var loaded = _store.LoadAll().Single();

        Assert.False(string.IsNullOrWhiteSpace(loaded.Id));
        Assert.False(string.IsNullOrWhiteSpace(loaded.Items[0].Id));
        // A request entry with no Request block must still be usable.
        Assert.NotNull(loaded.Items[0].Request);
    }

    [Fact]
    public void Settings_round_trip()
    {
        _store.SaveSettings(new AppSettings { LastCollectionId = "abc", LastSelectedNodeId = "def" });

        var settings = _store.LoadSettings();

        Assert.Equal("abc", settings.LastCollectionId);
        Assert.Equal("def", settings.LastSelectedNodeId);
    }

    [Fact]
    public void Missing_settings_fall_back_to_defaults()
    {
        var settings = _store.LoadSettings();

        Assert.Null(settings.LastCollectionId);
        Assert.Null(settings.LastSelectedNodeId);
    }

    [Fact]
    public void Collections_are_returned_in_name_order()
    {
        foreach (var name in new[] { "Zebra", "apple", "Mango" })
        {
            _store.Save(new CollectionViewModel { Name = name }.ToModel());
        }

        Assert.Equal(["apple", "Mango", "Zebra"], _store.LoadAll().Select(c => c.Name));
    }
}
