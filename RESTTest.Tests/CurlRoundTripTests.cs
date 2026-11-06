using RESTTest.Models;
using RESTTest.Services;

namespace RESTTest.Tests;

/// <summary>
/// Exports a request and reads the command back. If the two halves ever disagree about quoting,
/// encoding or which header is implied, a request would change as it passes through the clipboard.
/// </summary>
public class CurlRoundTripTests
{
    private const string BaseUrl = "https://api.example.com/v1";

    private readonly HttpRequestSender _sender = new();

    private RequestModel RoundTrip(RequestModel original)
    {
        var url = _sender.BuildUrl(BaseUrl, original);
        var command = CurlExporter.Build(url, original);

        Assert.True(CurlParser.TryParse(command, BaseUrl, out var import, out var error), $"{error}\n{command}");
        Assert.False(import!.KeptAbsoluteUrl);
        Assert.Empty(import.Warnings);
        return import.Request;
    }

    private static void AssertSameRows(IEnumerable<KeyValueModel> expected, IEnumerable<KeyValueModel> actual)
    {
        Assert.Equal(
            expected.Where(r => r.Enabled).Select(r => (r.Key, r.Value)),
            actual.Select(r => (r.Key, r.Value)));
    }

    [Fact]
    public void A_get_with_query_headers_and_bearer_auth_survives()
    {
        var original = new RequestModel
        {
            Method = "GET",
            Path = "/users",
            Auth = AuthKind.BearerToken,
            BearerToken = "abc.def.ghi",
            QueryParams =
            [
                new KeyValueModel { Key = "page", Value = "2" },
                new KeyValueModel { Key = "q", Value = "a b&c=d" },
                new KeyValueModel { Key = "off", Value = "x", Enabled = false },
            ],
            Headers = [new KeyValueModel { Key = "Accept", Value = "application/json" }],
        };

        var back = RoundTrip(original);

        Assert.Equal("GET", back.Method);
        Assert.Equal("/users", back.Path);
        Assert.Equal(AuthKind.BearerToken, back.Auth);
        Assert.Equal("abc.def.ghi", back.BearerToken);
        AssertSameRows(original.QueryParams, back.QueryParams);
        AssertSameRows(original.Headers, back.Headers);
    }

    [Theory]
    [InlineData(BodyKind.Json, "{\n  \"name\": \"it's \\\"fine\\\"\",\n  \"cmd\": \"$(x) `y` \\\\ !\",\n  \"café\": \"日本語\"\n}")]
    [InlineData(BodyKind.Text, "plain 'quoted' text\nwith a second line")]
    [InlineData(BodyKind.Xml, "<a b=\"1\"><c>it's</c></a>")]
    public void A_text_body_comes_back_character_for_character(BodyKind kind, string body)
    {
        var original = new RequestModel { Method = "POST", Path = "/things", BodyKind = kind, Body = body };

        var back = RoundTrip(original);

        Assert.Equal("POST", back.Method);
        Assert.Equal(kind, back.BodyKind);
        Assert.Equal(body, back.Body);
        Assert.Empty(back.Headers);
    }

    [Fact]
    public void A_form_comes_back_with_the_same_fields()
    {
        var original = new RequestModel
        {
            Method = "POST",
            Path = "/token",
            BodyKind = BodyKind.FormUrlEncoded,
            FormFields =
            [
                new KeyValueModel { Key = "grant_type", Value = "client_credentials" },
                new KeyValueModel { Key = "scope", Value = "read write" },
                new KeyValueModel { Key = "secret", Value = "a&b=c%d+e" },
            ],
        };

        var back = RoundTrip(original);

        Assert.Equal(BodyKind.FormUrlEncoded, back.BodyKind);
        AssertSameRows(original.FormFields, back.FormFields);
    }

    [Fact]
    public void A_multipart_comes_back_with_text_and_file_parts_in_order()
    {
        var original = new RequestModel
        {
            Method = "POST",
            Path = "/upload",
            BodyKind = BodyKind.Multipart,
            MultipartParts =
            [
                new MultipartPartModel { Key = "title", Value = "@not a file; really" },
                new MultipartPartModel { Key = "plain", Value = "report.pdf", IsFile = true },
                new MultipartPartModel { Key = "odd", Value = "/tmp/a;b,c.txt", IsFile = true },
                new MultipartPartModel { Key = "win", Value = @"C:\Users\ada\notes.txt", IsFile = true },
            ],
        };

        var back = RoundTrip(original);

        Assert.Equal(BodyKind.Multipart, back.BodyKind);
        Assert.Equal(
            original.MultipartParts.Select(p => (p.Key, p.Value, p.IsFile)),
            back.MultipartParts.Select(p => (p.Key, p.Value, p.IsFile)));
    }

    [Fact]
    public void A_user_set_content_type_header_survives_alongside_its_body()
    {
        var original = new RequestModel
        {
            Method = "PUT",
            Path = "/doc",
            Body = "{}",
            Headers = [new KeyValueModel { Key = "Content-Type", Value = "application/vnd.api+json" }],
        };

        var back = RoundTrip(original);

        Assert.Equal(BodyKind.Json, back.BodyKind);
        AssertSameRows(original.Headers, back.Headers);
    }

    [Fact]
    public void A_head_request_and_a_delete_keep_their_methods()
    {
        Assert.Equal("HEAD", RoundTrip(new RequestModel { Method = "HEAD", Path = "/ping" }).Method);
        Assert.Equal("DELETE", RoundTrip(new RequestModel { Method = "DELETE", Path = "/users/1" }).Method);
    }

    [Fact]
    public async Task The_exported_command_and_the_original_request_build_the_same_wire_content()
    {
        var original = new RequestModel
        {
            Method = "POST",
            Path = "/form",
            BodyKind = BodyKind.FormUrlEncoded,
            FormFields = [new KeyValueModel { Key = "a b", Value = "c&d" }, new KeyValueModel { Key = "e", Value = "f" }],
        };

        var back = RoundTrip(original);

        using var before = HttpRequestSender.BuildContent(original, "POST");
        using var after = HttpRequestSender.BuildContent(back, "POST");
        Assert.Equal(await before!.ReadAsStringAsync(), await after!.ReadAsStringAsync());
    }
}
