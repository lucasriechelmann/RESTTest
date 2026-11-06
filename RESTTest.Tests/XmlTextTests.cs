using RESTTest.Services;

namespace RESTTest.Tests;

public class XmlTextTests
{
    private static string Normalise(string text) => text.Replace("\r\n", "\n");

    [Fact]
    public void Format_indents_nested_elements_with_two_spaces()
    {
        Assert.True(XmlText.TryFormat("<a><b>1</b><c><d/></c></a>", out var formatted, out var error));

        Assert.Null(error);
        Assert.Equal("<a>\n  <b>1</b>\n  <c>\n    <d />\n  </c>\n</a>", Normalise(formatted));
    }

    [Fact]
    public void Formatting_twice_changes_nothing_the_second_time()
    {
        XmlText.TryFormat("<a><b>1</b></a>", out var once, out _);
        XmlText.TryFormat(once, out var twice, out _);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Minify_removes_the_whitespace_between_elements()
    {
        Assert.True(XmlText.TryMinify("<a>\n  <b>1</b>\n  <c />\n</a>", out var minified, out _));

        Assert.Equal("<a><b>1</b><c /></a>", minified);
    }

    [Fact]
    public void Text_inside_an_element_is_left_alone()
    {
        XmlText.TryFormat("<a>  spaced out  </a>", out var formatted, out _);

        Assert.Contains("  spaced out  ", formatted);
    }

    [Fact]
    public void A_declaration_is_kept_and_says_utf_8_because_that_is_what_is_sent()
    {
        XmlText.TryFormat("<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?><a/>", out var formatted, out _);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", formatted);
    }

    [Fact]
    public void No_declaration_is_added_when_there_was_none()
    {
        XmlText.TryFormat("<a/>", out var formatted, out _);

        Assert.DoesNotContain("<?xml", formatted);
    }

    [Fact]
    public void Comments_and_attributes_survive()
    {
        XmlText.TryFormat("<a id=\"1\"><!-- note --><b/></a>", out var formatted, out _);

        Assert.Contains("id=\"1\"", formatted);
        Assert.Contains("<!-- note -->", formatted);
    }

    [Theory]
    [InlineData("<a><b></a>")]
    [InlineData("<a>")]
    [InlineData("not xml at all")]
    [InlineData("<a></a><b></b>")]
    public void Malformed_xml_is_reported_with_a_position_and_the_text_is_untouched(string input)
    {
        Assert.False(XmlText.TryFormat(input, out var output, out var error));

        Assert.Contains("Invalid XML at line", error);
        Assert.Equal(input, output);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Nothing_to_format_is_not_an_error_that_throws(string? input)
    {
        Assert.False(XmlText.TryFormat(input, out _, out var error));
        Assert.Equal("There is nothing to format.", error);
    }
}
