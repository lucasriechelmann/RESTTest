using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using RESTTest.Models;
using RESTTest.Services;
using RESTTest.ViewModels;

namespace RESTTest.Tests;

/// <summary>Binary-safe response bodies, the proposed file name, and writing a response to disk.</summary>
public sealed class ResponseSavingTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "resttest-save-" + Guid.NewGuid().ToString("N"));

    public ResponseSavingTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0xFF, 0xFE];

    // ---- Text or binary ------------------------------------------------------------------------

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("application/problem+json")]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/xml")]
    [InlineData("application/javascript")]
    [InlineData("application/x-www-form-urlencoded")]
    public void Known_text_types_are_text_even_if_the_bytes_look_odd(string type)
    {
        Assert.False(ResponseBody.IsBinary([0x01, 0x00, 0x7B], type));
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("application/pdf")]
    [InlineData("application/zip")]
    [InlineData("application/octet-stream")]
    [InlineData("audio/mpeg")]
    [InlineData("application/vnd.ms-excel")]
    public void Known_binary_types_are_binary_even_if_the_bytes_happen_to_be_ascii(string type)
    {
        Assert.True(ResponseBody.IsBinary("plain ascii"u8.ToArray(), type));
    }

    [Fact]
    public void An_unknown_type_is_judged_by_its_bytes()
    {
        Assert.False(ResponseBody.IsBinary("héllo, wörld"u8.ToArray(), null));
        Assert.False(ResponseBody.IsBinary([], ""));
        Assert.True(ResponseBody.IsBinary(Png, null));
        Assert.True(ResponseBody.IsBinary([0x41, 0x00, 0x42], "application/x-custom"));
        Assert.True(ResponseBody.IsBinary([0xC3, 0x28, 0x41], "application/x-custom")); // invalid UTF-8
    }

    [Fact]
    public void A_long_text_cut_mid_character_by_the_sniff_window_is_still_text()
    {
        // 7999 ASCII bytes then a 2-byte character straddles the 8000-byte window.
        var bytes = Encoding.UTF8.GetBytes(new string('a', 7999) + "é" + new string('b', 100));

        Assert.False(ResponseBody.IsBinary(bytes, null));
    }

    [Fact]
    public void Text_is_decoded_with_the_declared_charset_and_a_bom_wins()
    {
        var latin1 = Encoding.Latin1.GetBytes("café");
        Assert.Equal("café", ResponseBody.Decode(latin1, "text/plain; charset=iso-8859-1", out var binary));
        Assert.False(binary);

        var withBom = Encoding.UTF8.GetPreamble().Concat("café"u8.ToArray()).ToArray();
        Assert.Equal("café", ResponseBody.Decode(withBom, "text/plain; charset=iso-8859-1", out _));

        Assert.Equal("café", ResponseBody.Decode("café"u8.ToArray(), "text/plain; charset=nonsense", out _));
    }

    [Fact]
    public void Binary_bodies_decode_to_nothing_and_say_so()
    {
        Assert.Equal(string.Empty, ResponseBody.Decode(Png, "image/png", out var binary));
        Assert.True(binary);
    }

    // ---- File names ----------------------------------------------------------------------------

    [Theory]
    [InlineData("attachment; filename=\"report.pdf\"", "report.pdf")]
    [InlineData("attachment; filename=report.pdf", "report.pdf")]
    [InlineData("attachment; filename=\"a b.txt\"; size=3", "a b.txt")]
    [InlineData("attachment; filename=\"plain.txt\"; filename*=UTF-8''r%C3%A9sum%C3%A9.txt", "résumé.txt")]
    [InlineData("attachment; filename=\"../../evil.exe\"", "evil.exe")]
    [InlineData("attachment; filename=\"bad:name?.txt\"", "badname.txt")]
    public void The_servers_file_name_is_used_and_made_safe(string disposition, string expected)
    {
        Assert.Equal(expected, ResponseFileName.Suggest("https://h.test/download/1", "application/pdf", disposition, true));
    }

    [Theory]
    [InlineData("https://h.test/files/logo.png", "image/png", true, "logo.png")]
    [InlineData("https://h.test/files/logo", "image/png", true, "logo.png")]
    [InlineData("https://h.test/users/7", "application/json", false, "7.json")]
    [InlineData("https://h.test/", "application/json", false, "response.json")]
    [InlineData("https://h.test/x", "application/x-custom", true, "x.bin")]
    [InlineData("https://h.test/x", "", false, "x.txt")]
    [InlineData("https://h.test/a%20b.pdf?token=1", "application/pdf", true, "a b.pdf")]
    [InlineData("https://h.test/feed", "application/vnd.api+json", false, "feed.json")]
    public void Without_a_server_name_the_file_name_comes_from_the_url_and_type(string url, string type, bool binary, string expected)
    {
        Assert.Equal(expected, ResponseFileName.Suggest(url, type, null, binary));
    }

    // ---- The sender ----------------------------------------------------------------------------

    private static async Task<string> ServeAsync(byte[] body, string contentType, string? extraHeaders = null)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        _ = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buffer = new byte[4096];
            _ = await stream.ReadAsync(buffer);

            var head = $"HTTP/1.1 200 OK\r\nContent-Type: {contentType}\r\nContent-Length: {body.Length}\r\n{extraHeaders}Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
            await stream.WriteAsync(body);
            listener.Stop();
        });

        await Task.CompletedTask;
        return $"http://127.0.0.1:{port}";
    }

    [Fact]
    public async Task A_binary_response_arrives_byte_for_byte_with_no_text()
    {
        var baseUrl = await ServeAsync(Png, "image/png");

        var result = await new HttpRequestSender().SendAsync(baseUrl, new RequestModel { Path = "/logo.png" }, CancellationToken.None);

        Assert.True(result.Completed, result.Error);
        Assert.True(result.IsBinary);
        Assert.Equal(Png, result.BodyBytes);
        Assert.Equal(string.Empty, result.Body);
        Assert.Equal(Png.Length, result.ContentLength);
    }

    [Fact]
    public async Task A_text_response_keeps_its_text_and_its_bytes()
    {
        var baseUrl = await ServeAsync("{\"ok\":true}"u8.ToArray(), "application/json");

        var result = await new HttpRequestSender().SendAsync(baseUrl, new RequestModel { Path = "/x" }, CancellationToken.None);

        Assert.False(result.IsBinary);
        Assert.Equal("{\"ok\":true}", result.Body);
        Assert.Equal("{\"ok\":true}"u8.ToArray(), result.BodyBytes);
    }

    // ---- Saving --------------------------------------------------------------------------------

    private static HttpExchangeResult Binary(string? disposition = null) => new()
    {
        Url = "https://h.test/files/logo.png",
        Method = "GET",
        Completed = true,
        StatusCode = 200,
        ContentType = "image/png",
        ContentLength = Png.Length,
        BodyBytes = Png,
        IsBinary = true,
        ResponseHeaders = disposition is null ? [] : [new KeyValueModel { Key = "Content-Disposition", Value = disposition }],
    };

    [Fact]
    public void Save_writes_the_exact_bytes_and_proposes_a_name()
    {
        var dialogs = new FakeDialogService { SavePathResult = Path.Combine(_dir, "out.png") };
        var response = new ResponseViewModel(dialogs);
        response.Load(Binary());

        Assert.True(response.CanSave);
        response.SaveToFileCommand.Execute(null);

        Assert.Equal(Png, File.ReadAllBytes(Path.Combine(_dir, "out.png")));
        Assert.Equal(["logo.png"], dialogs.SaveSuggestions);
        Assert.Contains("Saved", response.SaveNote);
        Assert.Contains("out.png", response.SaveNote);
    }

    [Fact]
    public void Save_prefers_the_servers_file_name()
    {
        var dialogs = new FakeDialogService { SavePathResult = null };
        var response = new ResponseViewModel(dialogs);
        response.Load(Binary("attachment; filename=\"avatar.png\""));

        response.SaveToFileCommand.Execute(null);

        Assert.Equal(["avatar.png"], dialogs.SaveSuggestions);
    }

    [Fact]
    public void Cancelling_the_dialog_writes_nothing_and_says_nothing()
    {
        var response = new ResponseViewModel(new FakeDialogService { SavePathResult = null });
        response.Load(Binary());

        response.SaveToFileCommand.Execute(null);

        Assert.Equal(string.Empty, response.SaveNote);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public void A_write_that_fails_is_explained_not_thrown()
    {
        // A directory where the file should go cannot be opened for writing.
        var dialogs = new FakeDialogService { SavePathResult = _dir };
        var response = new ResponseViewModel(dialogs);
        response.Load(Binary());

        response.SaveToFileCommand.Execute(null);

        Assert.Single(dialogs.Messages);
        Assert.Contains("could not be written", dialogs.Messages[0]);
        Assert.Equal(string.Empty, response.SaveNote);
    }

    [Fact]
    public void A_text_response_saves_as_utf8_even_when_built_without_bytes()
    {
        var dialogs = new FakeDialogService { SavePathResult = Path.Combine(_dir, "t.json") };
        var response = new ResponseViewModel(dialogs);
        response.Load(new HttpExchangeResult
        {
            Url = "https://h.test/users/7",
            Method = "GET",
            Completed = true,
            StatusCode = 200,
            ContentType = "application/json",
            Body = "{\"n\":\"é\"}",
        });

        response.SaveToFileCommand.Execute(null);

        Assert.Equal("{\"n\":\"é\"}", File.ReadAllText(Path.Combine(_dir, "t.json")));
        Assert.Equal(["7.json"], dialogs.SaveSuggestions);
    }

    [Fact]
    public void Nothing_can_be_saved_without_a_body_an_error_or_a_response()
    {
        var response = new ResponseViewModel(new FakeDialogService());
        Assert.False(response.SaveToFileCommand.CanExecute(null));

        response.Load(new HttpExchangeResult { Url = "u", Method = "GET", Completed = true, StatusCode = 204 });
        Assert.False(response.CanSave);

        response.Load(HttpExchangeResult.Failure("u", "GET", "refused", 1));
        Assert.False(response.CanSave);

        response.Load(Binary());
        Assert.True(response.SaveToFileCommand.CanExecute(null));

        response.Clear();
        Assert.False(response.CanSave);
        Assert.False(response.SaveToFileCommand.CanExecute(null));
    }

    [Fact]
    public void A_binary_response_shows_a_notice_and_a_text_one_does_not()
    {
        var response = new ResponseViewModel(new FakeDialogService());

        response.Load(Binary());
        Assert.True(response.IsBinary);
        Assert.Contains("Binary content", response.BinaryNotice);
        Assert.Contains("image/png", response.BinaryNotice);

        response.Load(new HttpExchangeResult { Url = "u", Method = "GET", Completed = true, StatusCode = 200, Body = "hi" });
        Assert.False(response.IsBinary);
        Assert.Equal(string.Empty, response.BinaryNotice);
    }

    [Fact]
    public void A_new_response_clears_the_save_note()
    {
        var response = new ResponseViewModel(new FakeDialogService { SavePathResult = Path.Combine(_dir, "a.png") });
        response.Load(Binary());
        response.SaveToFileCommand.Execute(null);
        Assert.NotEqual(string.Empty, response.SaveNote);

        response.Load(Binary());

        Assert.Equal(string.Empty, response.SaveNote);
    }
}
