using RESTTest.Services;

namespace RESTTest.Tests;

/// <summary>Covers the Format JSON / Minify buttons and the response pretty-printer.</summary>
public class JsonTextTests
{
    [Fact]
    public void Format_indents_with_two_spaces()
    {
        var ok = JsonText.TryFormat("{\"a\":1,\"b\":[2,3]}", out var formatted, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(
            """
            {
              "a": 1,
              "b": [
                2,
                3
              ]
            }
            """.ReplaceLineEndings(),
            formatted.ReplaceLineEndings());
    }

    [Fact]
    public void Format_leaves_non_ascii_and_quotes_readable()
    {
        // The relaxed encoder is what keeps saved bodies hand-editable rather than full of ".
        JsonText.TryFormat("""{"name":"Olá \"world\""}""", out var formatted, out _);

        Assert.Contains("Olá", formatted);
        Assert.Contains("\\\"world\\\"", formatted);
        Assert.DoesNotContain("\\u0022", formatted);
    }

    [Fact]
    public void Format_reports_the_position_of_a_syntax_error()
    {
        var ok = JsonText.TryFormat("{\"a\": }", out var output, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("line", error);
        // The caller's text must survive a failed format.
        Assert.Equal("{\"a\": }", output);
    }

    [Fact]
    public void Format_rejects_empty_input()
    {
        var ok = JsonText.TryFormat("   ", out _, out var error);

        Assert.False(ok);
        Assert.Equal("There is nothing to format.", error);
    }

    [Fact]
    public void Format_accepts_trailing_commas_and_comments()
    {
        var ok = JsonText.TryFormat("{\"a\":1, /* note */ }", out var formatted, out _);

        Assert.True(ok);
        Assert.Contains("\"a\": 1", formatted);
    }

    [Fact]
    public void Minify_collapses_to_one_line()
    {
        var ok = JsonText.TryMinify("{\n  \"a\": 1\n}", out var minified, out _);

        Assert.True(ok);
        Assert.Equal("{\"a\":1}", minified);
    }

    [Fact]
    public void PrettyOrOriginal_passes_non_json_through_untouched()
    {
        const string html = "<html><body>Not found</body></html>";

        Assert.Equal(html, JsonText.PrettyOrOriginal(html));
    }

    [Fact]
    public void PrettyOrOriginal_formats_json()
    {
        Assert.Contains("\n", JsonText.PrettyOrOriginal("{\"a\":1}"));
    }

    [Theory]
    [InlineData("[]", true)]
    [InlineData("null", true)]
    [InlineData("\"text\"", true)]
    [InlineData("{", false)]
    [InlineData("", false)]
    public void IsValid_recognises_json_documents(string input, bool expected) =>
        Assert.Equal(expected, JsonText.IsValid(input));
}
