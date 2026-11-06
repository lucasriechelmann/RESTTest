using RESTTest.Services;

namespace RESTTest.Tests;

public class JsonPathLiteTests
{
    private const string Body = """
        {
          "id": 7,
          "name": "Ada",
          "active": true,
          "nothing": null,
          "tags": ["a", "b"],
          "owner": { "login": "ada", "teams": [ { "name": "core" } ] },
          "a.b": "dotted"
        }
        """;

    [Theory]
    [InlineData("$.id", "7")]
    [InlineData("id", "7")]
    [InlineData("$.name", "Ada")]
    [InlineData("$.active", "true")]
    [InlineData("$.nothing", "null")]
    [InlineData("$.tags[1]", "b")]
    [InlineData("$.owner.login", "ada")]
    [InlineData("$.owner.teams[0].name", "core")]
    [InlineData("$['a.b']", "dotted")]
    [InlineData("$.tags", "[\"a\", \"b\"]")]
    public void Finds_the_value_at_a_path(string path, string expected)
    {
        Assert.True(JsonPathLite.TryGetValue(Body, path, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("$.missing")]
    [InlineData("$.tags[5]")]
    [InlineData("$.id.deeper")]
    [InlineData("$.name[0]")]
    [InlineData("$.tags[x]")]
    [InlineData("$.tags[1")]
    [InlineData("")]
    [InlineData("   ")]
    public void Reports_not_found_for_missing_or_malformed_paths(string path)
    {
        Assert.False(JsonPathLite.TryGetValue(Body, path, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>not json</html>")]
    [InlineData("{ broken")]
    public void Reports_not_found_when_the_body_is_not_json(string body)
    {
        Assert.False(JsonPathLite.TryGetValue(body, "$.id", out _));
    }

    [Fact]
    public void A_bare_dollar_selects_the_whole_document()
    {
        Assert.True(JsonPathLite.TryGetValue("[1,2]", "$", out var value));
        Assert.Equal("[1,2]", value);
    }

    [Fact]
    public void Property_names_are_case_sensitive()
    {
        Assert.False(JsonPathLite.TryGetValue(Body, "$.Name", out _));
    }

    [Fact]
    public void Parses_names_and_indexes_into_segments()
    {
        Assert.True(JsonPathLite.TryParse("$.a[2].b", out var segments));
        Assert.Equal(new object[] { "a", 2, "b" }, segments);
    }
}
