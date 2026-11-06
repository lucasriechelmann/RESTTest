using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using RESTTest.Models;
using RESTTest.Services;

namespace RESTTest.Tests;

/// <summary>
/// Covers how each body kind is turned into what goes on the wire: the Content-Type, the
/// encoding, what is skipped, and that uploaded files really are streamed from disk.
/// </summary>
public sealed class BodyBuildingTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "RestTestTests", Guid.NewGuid().ToString("N"));

    public BodyBuildingTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // A locked temp file must not fail the test run.
        }
    }

    private string TempFile(string name, string contents)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, contents);
        return path;
    }

    private static RequestModel Request(BodyKind kind, string body = "") => new()
    {
        Method = "POST",
        BodyKind = kind,
        Body = body,
    };

    private static async Task<(string Body, string? ContentType)> Read(HttpContent? content)
    {
        Assert.NotNull(content);
        return (await content.ReadAsStringAsync(), content.Headers.ContentType?.ToString());
    }

    [Theory]
    [InlineData(BodyKind.Json, "application/json")]
    [InlineData(BodyKind.Text, "text/plain")]
    [InlineData(BodyKind.Xml, "application/xml")]
    public async Task Text_kinds_send_the_body_with_their_own_content_type(BodyKind kind, string mediaType)
    {
        using var content = HttpRequestSender.BuildContent(Request(kind, "payload"), "POST");

        var (body, contentType) = await Read(content);

        Assert.Equal("payload", body);
        Assert.StartsWith(mediaType, contentType);
        Assert.Contains("utf-8", contentType, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(BodyKind.Json)]
    [InlineData(BodyKind.Text)]
    [InlineData(BodyKind.Xml)]
    public void A_text_kind_with_no_text_sends_nothing(BodyKind kind)
    {
        Assert.Null(HttpRequestSender.BuildContent(Request(kind, "   "), "POST"));
    }

    [Fact]
    public void None_sends_nothing_even_when_text_is_kept()
    {
        Assert.Null(HttpRequestSender.BuildContent(Request(BodyKind.None, "still here"), "POST"));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public void Methods_that_carry_no_body_send_none_whatever_the_kind(string method)
    {
        var request = Request(BodyKind.Json, "{}");
        request.FormFields = [new KeyValueModel { Key = "a", Value = "b" }];
        request.MultipartParts = [new MultipartPartModel { Key = "a", Value = "b" }];

        Assert.Null(HttpRequestSender.BuildContent(request, method));

        request.BodyKind = BodyKind.FormUrlEncoded;
        Assert.Null(HttpRequestSender.BuildContent(request, method));

        request.BodyKind = BodyKind.Multipart;
        Assert.Null(HttpRequestSender.BuildContent(request, method));
    }

    [Fact]
    public async Task A_form_body_is_urlencoded_and_skips_disabled_and_unnamed_rows()
    {
        var request = Request(BodyKind.FormUrlEncoded);
        request.FormFields =
        [
            new KeyValueModel { Key = "name", Value = "Ada Lovelace" },
            new KeyValueModel { Key = "q", Value = "a&b=c" },
            new KeyValueModel { Key = "off", Value = "1", Enabled = false },
            new KeyValueModel { Key = "  ", Value = "nameless" },
            new KeyValueModel { Key = " trimmed ", Value = "" },
        ];

        using var content = HttpRequestSender.BuildContent(request, "POST");
        var (body, contentType) = await Read(content);

        Assert.Equal("name=Ada+Lovelace&q=a%26b%3Dc&trimmed=", body);
        Assert.StartsWith("application/x-www-form-urlencoded", contentType);
    }

    [Fact]
    public void A_form_body_with_no_usable_rows_sends_nothing()
    {
        var request = Request(BodyKind.FormUrlEncoded);
        request.FormFields = [new KeyValueModel { Key = "off", Enabled = false }];

        Assert.Null(HttpRequestSender.BuildContent(request, "POST"));
    }

    [Fact]
    public async Task A_multipart_body_carries_text_fields_and_streams_files()
    {
        var path = TempFile("notes.txt", "hello from a file");
        var request = Request(BodyKind.Multipart);
        request.MultipartParts =
        [
            new MultipartPartModel { Key = "title", Value = "My upload" },
            new MultipartPartModel { Key = "attachment", Value = path, IsFile = true },
            new MultipartPartModel { Key = "skipped", Value = "x", Enabled = false },
        ];

        using var content = HttpRequestSender.BuildContent(request, "POST");
        var (body, contentType) = await Read(content);

        Assert.StartsWith("multipart/form-data; boundary=", contentType);
        Assert.Contains("name=title", body);
        Assert.Contains("My upload", body);
        Assert.Contains("name=attachment", body);
        Assert.Contains("filename=notes.txt", body);
        Assert.Contains("Content-Type: text/plain", body);
        Assert.Contains("hello from a file", body);
        Assert.DoesNotContain("skipped", body);

        // The text field is untyped; only the file carries a Content-Type of its own.
        var titlePart = body.Split("\r\n--").First(part => part.Contains("name=title"));
        Assert.DoesNotContain("Content-Type", titlePart);
    }

    [Fact]
    public async Task A_file_with_an_unknown_extension_is_sent_as_octet_stream()
    {
        var path = TempFile("data.weird", "bytes");
        var request = Request(BodyKind.Multipart);
        request.MultipartParts = [new MultipartPartModel { Key = "f", Value = path, IsFile = true }];

        using var content = HttpRequestSender.BuildContent(request, "POST");
        var (body, _) = await Read(content);

        Assert.Contains("Content-Type: application/octet-stream", body);
    }

    [Fact]
    public void A_missing_file_is_reported_by_field_name_and_path()
    {
        var request = Request(BodyKind.Multipart);
        request.MultipartParts =
        [
            new MultipartPartModel { Key = "attachment", Value = Path.Combine(_folder, "gone.bin"), IsFile = true },
        ];

        var error = Assert.Throws<FileNotFoundException>(() => HttpRequestSender.BuildContent(request, "POST"));

        Assert.Contains("'attachment'", error.Message);
        Assert.Contains("gone.bin", error.Message);
    }

    [Fact]
    public void A_file_part_with_no_file_chosen_says_so()
    {
        var request = Request(BodyKind.Multipart);
        request.MultipartParts = [new MultipartPartModel { Key = "attachment", IsFile = true }];

        var error = Assert.Throws<FileNotFoundException>(() => HttpRequestSender.BuildContent(request, "POST"));

        Assert.Contains("no file chosen", error.Message);
    }

    [Fact]
    public void A_missing_second_file_does_not_leave_the_first_one_locked()
    {
        var first = TempFile("first.txt", "one");
        var request = Request(BodyKind.Multipart);
        request.MultipartParts =
        [
            new MultipartPartModel { Key = "a", Value = first, IsFile = true },
            new MultipartPartModel { Key = "b", Value = Path.Combine(_folder, "missing.txt"), IsFile = true },
        ];

        Assert.Throws<FileNotFoundException>(() => HttpRequestSender.BuildContent(request, "POST"));

        // Would throw IOException if the first file's handle had been leaked.
        File.Delete(first);
        Assert.False(File.Exists(first));
    }

    [Fact]
    public void A_multipart_body_with_no_usable_parts_sends_nothing()
    {
        var request = Request(BodyKind.Multipart);
        request.MultipartParts = [new MultipartPartModel { Key = "", Value = "x" }];

        Assert.Null(HttpRequestSender.BuildContent(request, "POST"));
    }

    [Theory]
    [InlineData("a.json", "application/json")]
    [InlineData("A.PNG", "image/png")]
    [InlineData("photo.JPEG", "image/jpeg")]
    [InlineData("noextension", "application/octet-stream")]
    [InlineData("archive.tar.gz", "application/octet-stream")]
    public void Mime_types_come_from_the_extension_ignoring_case(string file, string expected)
    {
        Assert.Equal(expected, MimeTypes.For(file));
    }

    // ---- What actually reaches a socket -------------------------------------------------

    /// <summary>
    /// A one-shot HTTP server on loopback that records the raw request and answers 200. Proves the
    /// bytes on the wire, which a look at the HttpContent object cannot.
    /// </summary>
    private static async Task<(string BaseUrl, Task<string> Captured)> StartCapturingServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var captured = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var received = new MemoryStream();
            var buffer = new byte[8192];

            while (true)
            {
                var read = await stream.ReadAsync(buffer);
                if (read == 0)
                {
                    break;
                }

                received.Write(buffer, 0, read);
                var text = Encoding.UTF8.GetString(received.ToArray());
                var headerEnd = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);

                if (headerEnd < 0)
                {
                    continue;
                }

                var lengthLine = text[..headerEnd]
                    .Split("\r\n")
                    .FirstOrDefault(l => l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));

                var chunked = text[..headerEnd].Contains("Transfer-Encoding: chunked", StringComparison.OrdinalIgnoreCase);

                if (lengthLine is not null)
                {
                    var length = int.Parse(lengthLine["Content-Length:".Length..].Trim());
                    if (Encoding.UTF8.GetByteCount(text[(headerEnd + 4)..]) >= length)
                    {
                        break;
                    }
                }
                else if (!chunked || text.EndsWith("\r\n0\r\n\r\n", StringComparison.Ordinal))
                {
                    break;
                }
            }

            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"));
            listener.Stop();
            return Encoding.UTF8.GetString(received.ToArray());
        });

        await Task.CompletedTask;
        return ($"http://127.0.0.1:{port}", captured);
    }

    [Fact]
    public async Task A_multipart_upload_reaches_the_server_with_the_file_contents()
    {
        var path = TempFile("report.csv", "id,name\n1,Ada\n");
        var request = Request(BodyKind.Multipart);
        request.Path = "/upload";
        request.MultipartParts =
        [
            new MultipartPartModel { Key = "kind", Value = "monthly" },
            new MultipartPartModel { Key = "file", Value = path, IsFile = true },
        ];
        var (baseUrl, captured) = await StartCapturingServer();

        var result = await new HttpRequestSender().SendAsync(baseUrl, request, CancellationToken.None);
        var raw = await captured;

        Assert.True(result.Completed, result.Error);
        Assert.Equal(200, result.StatusCode);
        Assert.StartsWith("POST /upload", raw);
        Assert.Contains("multipart/form-data; boundary=", raw);
        Assert.Contains("name=kind", raw);
        Assert.Contains("monthly", raw);
        Assert.Contains("filename=report.csv", raw);
        Assert.Contains("Content-Type: text/csv", raw);
        Assert.Contains("id,name\n1,Ada\n", raw);

        // Only the file part is typed. The text field must not carry its own Content-Type.
        Assert.DoesNotContain("charset=utf-8", raw);
        Assert.Equal(1, raw.Split("Content-Type:").Length - 2);
    }

    [Fact]
    public async Task A_form_post_reaches_the_server_urlencoded()
    {
        var request = Request(BodyKind.FormUrlEncoded);
        request.Path = "/token";
        request.FormFields =
        [
            new KeyValueModel { Key = "grant_type", Value = "client_credentials" },
            new KeyValueModel { Key = "scope", Value = "read write" },
        ];
        var (baseUrl, captured) = await StartCapturingServer();

        var result = await new HttpRequestSender().SendAsync(baseUrl, request, CancellationToken.None);
        var raw = await captured;

        Assert.True(result.Completed, result.Error);
        Assert.Contains("Content-Type: application/x-www-form-urlencoded", raw);
        Assert.EndsWith("grant_type=client_credentials&scope=read+write", raw);
    }

    [Fact]
    public async Task A_missing_upload_file_comes_back_as_a_failed_exchange_not_an_exception()
    {
        var request = Request(BodyKind.Multipart);
        request.MultipartParts = [new MultipartPartModel { Key = "file", Value = Path.Combine(_folder, "nope.bin"), IsFile = true }];

        var result = await new HttpRequestSender().SendAsync("http://127.0.0.1:9", request, CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Contains("nope.bin", result.Error);
    }
}
