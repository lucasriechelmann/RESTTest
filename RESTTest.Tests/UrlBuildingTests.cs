using RESTTest.Models;
using RESTTest.Services;

namespace RESTTest.Tests;

/// <summary>
/// Covers the join between a collection's base URL and a request's path. This is the heart of
/// the "one URL per collection" design, and the place where a naive <c>new Uri(base, path)</c>
/// would silently drop part of the host's own path.
/// </summary>
public class UrlBuildingTests
{
    private readonly HttpRequestSender _sender = new();

    private static RequestModel Request(string path, params KeyValueModel[] query) => new()
    {
        Method = "GET",
        Path = path,
        QueryParams = [.. query],
    };

    [Theory]
    [InlineData("http://localhost:5000", "/posts", "http://localhost:5000/posts")]
    [InlineData("http://localhost:5000/", "/posts", "http://localhost:5000/posts")]
    [InlineData("http://localhost:5000", "posts", "http://localhost:5000/posts")]
    [InlineData("http://localhost:5000/", "posts", "http://localhost:5000/posts")]
    [InlineData("  http://localhost:5000  ", "  /posts  ", "http://localhost:5000/posts")]
    public void Joins_base_and_path_regardless_of_slashes(string baseUrl, string path, string expected) =>
        Assert.Equal(expected, _sender.BuildUrl(baseUrl, Request(path)));

    [Fact]
    public void Keeps_the_base_urls_own_path_segment()
    {
        // The regression this guards: new Uri(base, "/users") would yield ".../users",
        // throwing away the "/api/v1" the user deliberately put in the environment.
        var url = _sender.BuildUrl("https://example.com/api/v1", Request("/users"));

        Assert.Equal("https://example.com/api/v1/users", url);
    }

    [Fact]
    public void Empty_path_returns_the_base_url() =>
        Assert.Equal("https://example.com", _sender.BuildUrl("https://example.com/", Request(string.Empty)));

    [Fact]
    public void Absolute_path_overrides_the_base_url() =>
        Assert.Equal(
            "https://other.test/thing",
            _sender.BuildUrl("https://example.com", Request("https://other.test/thing")));

    [Fact]
    public void Missing_base_url_still_builds_from_an_absolute_path() =>
        Assert.Equal("https://other.test/x", _sender.BuildUrl(null, Request("https://other.test/x")));

    [Fact]
    public void Appends_enabled_query_parameters()
    {
        var url = _sender.BuildUrl(
            "https://example.com",
            Request("/search",
                new KeyValueModel { Enabled = true, Key = "q", Value = "cats" },
                new KeyValueModel { Enabled = true, Key = "page", Value = "2" }));

        Assert.Equal("https://example.com/search?q=cats&page=2", url);
    }

    [Fact]
    public void Skips_disabled_and_unnamed_query_parameters()
    {
        var url = _sender.BuildUrl(
            "https://example.com",
            Request("/search",
                new KeyValueModel { Enabled = false, Key = "draft", Value = "1" },
                new KeyValueModel { Enabled = true, Key = "   ", Value = "orphan" },
                new KeyValueModel { Enabled = true, Key = "q", Value = "ok" }));

        Assert.Equal("https://example.com/search?q=ok", url);
    }

    [Fact]
    public void Escapes_parameter_keys_and_values()
    {
        var url = _sender.BuildUrl(
            "https://example.com",
            Request("/search", new KeyValueModel { Enabled = true, Key = "a b", Value = "x&y=z" }));

        Assert.Equal("https://example.com/search?a%20b=x%26y%3Dz", url);
    }

    [Fact]
    public void Uses_an_ampersand_when_the_path_already_has_a_query()
    {
        var url = _sender.BuildUrl(
            "https://example.com",
            Request("/search?existing=1", new KeyValueModel { Enabled = true, Key = "q", Value = "ok" }));

        Assert.Equal("https://example.com/search?existing=1&q=ok", url);
    }

    [Fact]
    public void Empty_value_is_kept_so_flag_style_parameters_work()
    {
        var url = _sender.BuildUrl(
            "https://example.com",
            Request("/x", new KeyValueModel { Enabled = true, Key = "verbose", Value = "" }));

        Assert.Equal("https://example.com/x?verbose=", url);
    }

    [Fact]
    public async Task Reports_a_failure_instead_of_throwing_on_an_unusable_url()
    {
        var result = await _sender.SendAsync("not a url", Request("/x"), CancellationToken.None);

        Assert.False(result.Completed);
        Assert.NotNull(result.Error);
    }
}
