using RESTTest.Models;
using RESTTest.Services;

namespace RESTTest.Tests;

public class CurlExporterTests
{
    private const string Url = "https://api.example.com/v1/users?page=2";

    private static string Export(RequestModel request, string url = Url) => CurlExporter.Build(url, request);

    [Fact]
    public void A_plain_get_is_just_curl_and_the_url()
    {
        Assert.Equal($"curl '{Url}'", Export(new RequestModel { Method = "GET" }));
    }

    [Fact]
    public void Options_go_on_their_own_continued_lines()
    {
        var command = Export(new RequestModel
        {
            Method = "POST",
            Body = "{}",
            Headers = [new KeyValueModel { Key = "X-Id", Value = "1" }],
        });

        Assert.Equal(
            $"curl -X POST '{Url}' \\\n  -H 'X-Id: 1' \\\n  -H 'Content-Type: application/json' \\\n  --data-raw '{{}}'",
            command);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public void Other_methods_are_named_with_x(string method)
    {
        Assert.StartsWith($"curl -X {method} '", Export(new RequestModel { Method = method }));
    }

    [Fact]
    public void Head_uses_the_head_flag_because_x_head_would_hang()
    {
        Assert.StartsWith("curl -I '", Export(new RequestModel { Method = "HEAD" }));
    }

    [Fact]
    public void A_lower_case_method_is_normalised()
    {
        Assert.StartsWith("curl -X POST", Export(new RequestModel { Method = "post" }));
    }

    [Fact]
    public void A_single_quote_in_a_value_is_closed_escaped_and_reopened()
    {
        var command = Export(new RequestModel { Method = "POST", BodyKind = BodyKind.Text, Body = "it's" });

        Assert.Contains("--data-raw 'it'\\''s'", command);
    }

    [Fact]
    public void A_body_with_newlines_and_shell_characters_stays_inside_the_quotes_untouched()
    {
        var body = "{\n  \"cmd\": \"$(rm -rf /) `x` $HOME \\\\ !\"\n}";

        var command = Export(new RequestModel { Method = "POST", Body = body });

        Assert.Contains($"--data-raw '{body}'", command);
    }

    [Theory]
    [InlineData(BodyKind.Json, "application/json")]
    [InlineData(BodyKind.Text, "text/plain")]
    [InlineData(BodyKind.Xml, "application/xml")]
    public void Text_bodies_name_their_content_type(BodyKind kind, string type)
    {
        var command = Export(new RequestModel { Method = "POST", BodyKind = kind, Body = "x" });

        Assert.Contains($"-H 'Content-Type: {type}'", command);
        Assert.Contains("--data-raw 'x'", command);
    }

    [Fact]
    public void A_content_type_header_the_user_set_is_not_repeated()
    {
        var command = Export(new RequestModel
        {
            Method = "POST",
            Body = "{}",
            Headers = [new KeyValueModel { Key = "content-type", Value = "application/vnd.api+json" }],
        });

        // Header names are case-insensitive, so count them that way.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(command, "content-type", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        Assert.Contains("-H 'content-type: application/vnd.api+json'", command);
        Assert.DoesNotContain("-H 'Content-Type: application/json'", command);
    }

    [Fact]
    public void An_empty_text_body_adds_neither_data_nor_a_content_type()
    {
        var command = Export(new RequestModel { Method = "POST", Body = "  " });

        Assert.DoesNotContain("--data-raw", command);
        Assert.DoesNotContain("Content-Type", command);
    }

    [Fact]
    public void No_body_sends_no_data_even_if_text_is_kept()
    {
        var command = Export(new RequestModel { Method = "POST", BodyKind = BodyKind.None, Body = "kept" });

        Assert.DoesNotContain("kept", command);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public void A_method_that_sends_no_body_exports_none(string method)
    {
        var command = Export(new RequestModel { Method = method, Body = "{}" });

        Assert.DoesNotContain("--data-raw", command);
        Assert.DoesNotContain("Content-Type", command);
    }

    [Fact]
    public void Bearer_auth_is_an_authorization_header_first()
    {
        var command = Export(new RequestModel
        {
            Method = "GET",
            Auth = AuthKind.BearerToken,
            BearerToken = "  tok  ",
            Headers = [new KeyValueModel { Key = "X-Id", Value = "1" }],
        });

        Assert.Equal($"curl '{Url}' \\\n  -H 'Authorization: Bearer tok' \\\n  -H 'X-Id: 1'", command);
    }

    [Fact]
    public void An_authorization_header_replaces_the_bearer_one_as_it_does_when_sending()
    {
        var command = Export(new RequestModel
        {
            Method = "GET",
            Auth = AuthKind.BearerToken,
            BearerToken = "tok",
            Headers = [new KeyValueModel { Key = "Authorization", Value = "Basic abc" }],
        });

        Assert.Contains("-H 'Authorization: Basic abc'", command);
        Assert.DoesNotContain("Bearer", command);
    }

    [Fact]
    public void Disabled_and_unnamed_headers_are_left_out()
    {
        var command = Export(new RequestModel
        {
            Method = "GET",
            Headers =
            [
                new KeyValueModel { Key = "X-On", Value = "1" },
                new KeyValueModel { Key = "X-Off", Value = "1", Enabled = false },
                new KeyValueModel { Key = "  ", Value = "nameless" },
            ],
        });

        Assert.Contains("X-On", command);
        Assert.DoesNotContain("X-Off", command);
        Assert.DoesNotContain("nameless", command);
    }

    [Fact]
    public void A_form_uses_data_urlencode_and_leaves_the_content_type_to_curl()
    {
        var command = Export(new RequestModel
        {
            Method = "POST",
            BodyKind = BodyKind.FormUrlEncoded,
            FormFields =
            [
                new KeyValueModel { Key = "user", Value = "ada lovelace" },
                new KeyValueModel { Key = "my key", Value = "a&b=c" },
                new KeyValueModel { Key = "off", Value = "x", Enabled = false },
            ],
        });

        Assert.Contains("--data-urlencode 'user=ada lovelace'", command);
        Assert.Contains("--data-urlencode 'my%20key=a&b=c'", command);
        Assert.DoesNotContain("off", command);
        Assert.DoesNotContain("Content-Type", command);
    }

    [Fact]
    public void A_multipart_uses_form_string_for_text_and_f_for_files_with_their_type()
    {
        var command = Export(new RequestModel
        {
            Method = "POST",
            BodyKind = BodyKind.Multipart,
            MultipartParts =
            [
                new MultipartPartModel { Key = "title", Value = "@looks-like-a-file" },
                new MultipartPartModel { Key = "doc", Value = @"C:\x\report.pdf", IsFile = true },
                new MultipartPartModel { Key = "skip", Value = "x", Enabled = false },
            ],
        });

        Assert.Contains("--form-string 'title=@looks-like-a-file'", command);
        Assert.Contains("-F 'doc=@C:\\x\\report.pdf;type=application/pdf'", command);
        Assert.DoesNotContain("skip", command);
        Assert.DoesNotContain("Content-Type", command);
    }

    [Fact]
    public void A_file_path_containing_a_semicolon_is_quoted_for_curl()
    {
        var command = Export(new RequestModel
        {
            Method = "POST",
            BodyKind = BodyKind.Multipart,
            MultipartParts = [new MultipartPartModel { Key = "f", Value = "/tmp/a;b.txt", IsFile = true }],
        });

        Assert.Contains("-F 'f=@\"/tmp/a;b.txt\";type=text/plain'", command);
    }
}
