using System.IO;
using RESTTest.Models;
using RESTTest.Services;

namespace RESTTest.Tests;

/// <summary>Round-trips history through its JSON file in a temporary workspace.</summary>
public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _workspace;
    private readonly HistoryStore _store;

    public HistoryStoreTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "RestTestTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
        _store = new HistoryStore(_workspace);
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

    private static HistoryEntryModel Entry() => new()
    {
        Timestamp = new DateTimeOffset(2026, 11, 6, 9, 30, 0, TimeSpan.FromHours(1)),
        CollectionName = "Sample",
        Url = "https://api.example.com/things?q=1",
        Completed = true,
        StatusCode = 201,
        ReasonPhrase = "Created",
        ElapsedMilliseconds = 87,
        ContentLength = 12,
        Body = "{\"a\": \"quoted \\\"x\\\"\"}",
        BodyTruncated = true,
        TestsPassed = 1,
        TestsTotal = 2,
        ResponseHeaders = [new KeyValueModel { Key = "ETag", Value = "abc" }],
        Request = new RequestModel
        {
            Method = "POST",
            Path = "/things",
            Body = "{\"n\":1}",
            Auth = AuthKind.BearerToken,
            BearerToken = "t",
            Headers = [new KeyValueModel { Key = "X-One", Value = "1", Enabled = false }],
            Assertions = [new AssertionModel { Target = AssertionTarget.JsonPath, Selector = "$.a", Operator = AssertionOperator.Exists }],
        },
    };

    [Fact]
    public void An_entry_survives_a_save_and_load_unchanged()
    {
        var original = Entry();

        _store.Save([original]);
        var loaded = Assert.Single(_store.Load());

        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal(original.Timestamp, loaded.Timestamp);
        Assert.Equal("Sample", loaded.CollectionName);
        Assert.Equal(original.Url, loaded.Url);
        Assert.Equal(201, loaded.StatusCode);
        Assert.Equal(original.Body, loaded.Body);
        Assert.True(loaded.BodyTruncated);
        Assert.Equal((1, 2), (loaded.TestsPassed, loaded.TestsTotal));
        Assert.Equal("ETag", Assert.Single(loaded.ResponseHeaders).Key);

        Assert.Equal("POST", loaded.Request.Method);
        Assert.Equal(AuthKind.BearerToken, loaded.Request.Auth);
        Assert.False(Assert.Single(loaded.Request.Headers).Enabled);
        var assertion = Assert.Single(loaded.Request.Assertions);
        Assert.Equal("$.a", assertion.Selector);
        Assert.Equal(AssertionOperator.Exists, assertion.Operator);
    }

    [Fact]
    public void The_order_of_entries_is_preserved()
    {
        var first = Entry();
        var second = Entry();

        _store.Save([first, second]);

        Assert.Equal([first.Id, second.Id], _store.Load().Select(e => e.Id));
    }

    [Fact]
    public void Enums_are_written_as_names_and_the_file_stays_readable()
    {
        _store.Save([Entry()]);

        var text = File.ReadAllText(Path.Combine(_workspace, "history.json"));

        Assert.Contains("\"BearerToken\"", text);
        Assert.Contains("\"JsonPath\"", text);
        // The relaxed encoder keeps quotes as \" instead of the unreadable ".
        Assert.DoesNotContain("\\u0022", text);
        Assert.Contains("quoted", text);
    }

    [Fact]
    public void A_missing_file_loads_as_empty()
    {
        Assert.Empty(_store.Load());
    }

    [Fact]
    public void A_damaged_file_loads_as_empty_and_the_next_save_replaces_it()
    {
        var path = Path.Combine(_workspace, "history.json");
        File.WriteAllText(path, "{ this is not json");

        Assert.Empty(_store.Load());

        _store.Save([Entry()]);
        Assert.Single(_store.Load());
    }

    [Fact]
    public void Saving_creates_the_workspace_folder_if_it_is_gone()
    {
        var nested = Path.Combine(_workspace, "later", "deeper");
        var store = new HistoryStore(nested);

        store.Save([Entry()]);

        Assert.Single(store.Load());
    }

    [Fact]
    public void Saving_an_empty_list_clears_the_history()
    {
        _store.Save([Entry()]);
        _store.Save([]);

        Assert.Empty(_store.Load());
    }
}
