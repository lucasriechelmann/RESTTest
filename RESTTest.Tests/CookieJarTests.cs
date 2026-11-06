using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using RESTTest.Models;
using RESTTest.Services;
using RESTTest.ViewModels;

namespace RESTTest.Tests;

/// <summary>The cookie jar, its file, its dialog's view model, and cookies on the wire.</summary>
public sealed class CookieJarTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "resttest-cookies-" + Guid.NewGuid().ToString("N"));

    public CookieJarTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly Uri Site = new("http://example.test/app/page");

    private static CookieJar JarWith(params (string Name, string Value)[] cookies)
    {
        var jar = new CookieJar();
        foreach (var (name, value) in cookies)
        {
            jar.Container.Add(Site, new Cookie(name, value));
        }

        return jar;
    }

    // ---- Jar -----------------------------------------------------------------------------------

    [Fact]
    public void The_jar_starts_enabled_and_empty()
    {
        var jar = new CookieJar();

        Assert.True(jar.Enabled);
        Assert.Empty(jar.Snapshot());
        Assert.Null(jar.HeaderFor(Site));
    }

    [Fact]
    public void The_header_lists_the_cookies_for_a_url_and_respects_secure_and_path()
    {
        var jar = new CookieJar();
        jar.Container.Add(Site, new Cookie("a", "1", "/"));
        jar.Container.Add(Site, new Cookie("b", "2", "/app"));
        jar.Container.Add(Site, new Cookie("c", "3", "/other"));
        jar.Container.Add(Site, new Cookie("s", "x", "/") { Secure = true });

        var header = jar.HeaderFor(Site);

        Assert.Contains("a=1", header);
        Assert.Contains("b=2", header);
        Assert.DoesNotContain("c=3", header);
        Assert.DoesNotContain("s=x", header);
        Assert.Contains("s=x", jar.HeaderFor(new Uri("https://example.test/app/page")));
        Assert.Null(jar.HeaderFor(new Uri("http://elsewhere.test/")));
    }

    [Fact]
    public void Snapshot_is_ordered_by_host_path_and_name()
    {
        var jar = new CookieJar();
        jar.Container.Add(new Uri("http://b.test/"), new Cookie("z", "1"));
        jar.Container.Add(new Uri("http://a.test/"), new Cookie("y", "1"));
        jar.Container.Add(new Uri("http://a.test/"), new Cookie("x", "1"));

        Assert.Equal(["a.test:x", "a.test:y", "b.test:z"], jar.Snapshot().Select(c => $"{c.Domain.TrimStart('.')}:{c.Name}"));
    }

    [Fact]
    public void Remove_deletes_only_that_cookie_and_raises_changed()
    {
        var jar = JarWith(("a", "1"), ("b", "2"));
        var changed = 0;
        jar.Changed += (_, _) => changed++;

        jar.Remove(jar.Snapshot().First(c => c.Name == "a"));

        Assert.Equal(["b"], jar.Snapshot().Select(c => c.Name));
        Assert.Equal(1, changed);
        Assert.DoesNotContain("a=1", jar.HeaderFor(Site));
    }

    [Fact]
    public void Clear_empties_the_jar_and_raises_changed_once()
    {
        var jar = JarWith(("a", "1"), ("b", "2"));
        var changed = 0;
        jar.Changed += (_, _) => changed++;

        jar.Clear();

        Assert.Empty(jar.Snapshot());
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Switching_the_jar_on_or_off_raises_changed_only_when_it_flips()
    {
        var jar = new CookieJar();
        var changed = 0;
        jar.Changed += (_, _) => changed++;

        jar.Enabled = true;
        jar.Enabled = false;
        jar.Enabled = false;
        jar.Enabled = true;

        Assert.Equal(2, changed);
    }

    [Fact]
    public void NotifyMaybeChanged_fires_for_a_new_or_changed_cookie_and_stays_quiet_otherwise()
    {
        var jar = new CookieJar();
        var changed = 0;
        jar.Changed += (_, _) => changed++;

        jar.NotifyMaybeChanged();
        Assert.Equal(0, changed);

        jar.Container.Add(Site, new Cookie("a", "1"));
        jar.NotifyMaybeChanged();
        jar.NotifyMaybeChanged();
        Assert.Equal(1, changed);

        jar.Container.Add(Site, new Cookie("a", "2"));
        jar.NotifyMaybeChanged();
        Assert.Equal(2, changed);
    }

    [Fact]
    public void A_jar_survives_a_round_trip_through_its_model()
    {
        var jar = new CookieJar { Enabled = false };
        jar.Container.Add(Site, new Cookie("session", "abc") { HttpOnly = true, Expires = DateTime.Now.AddDays(2) });
        jar.Container.Add(Site, new Cookie("plain", "x") { Secure = true });

        var restored = new CookieJar();
        restored.Load(jar.ToModel());

        Assert.False(restored.Enabled);
        var session = Assert.Single(restored.Snapshot(), c => c.Name == "session");
        Assert.True(session.HttpOnly);
        Assert.NotNull(session.Expires);
        Assert.Null(restored.Snapshot().Single(c => c.Name == "plain").Expires);
        Assert.True(restored.Snapshot().Single(c => c.Name == "plain").Secure);
        Assert.Equal(jar.Snapshot().Count, restored.Snapshot().Count);
    }

    [Fact]
    public void Loading_skips_expired_and_malformed_cookies_without_raising_changed()
    {
        var jar = new CookieJar();
        var changed = 0;
        jar.Changed += (_, _) => changed++;

        jar.Load(new CookieJarModel
        {
            Cookies =
            [
                new CookieModel { Name = "old", Value = "1", Domain = "example.test", Expires = DateTime.UtcNow.AddDays(-1) },
                new CookieModel { Name = "", Value = "1", Domain = "example.test" },
                new CookieModel { Name = "nodomain", Value = "1", Domain = "" },
                new CookieModel { Name = "good", Value = "1", Domain = "example.test", Expires = DateTime.UtcNow.AddDays(1) },
            ],
        });

        Assert.Equal(["good"], jar.Snapshot().Select(c => c.Name));
        Assert.Equal(0, changed);
    }

    // ---- File ----------------------------------------------------------------------------------

    [Fact]
    public void The_store_round_trips_and_names_the_file_cookies_json()
    {
        var store = new CookieStore(_dir);
        store.Save(new CookieJarModel
        {
            Enabled = false,
            Cookies = [new CookieModel { Name = "a", Value = "1", Domain = "h.test", Path = "/p", Secure = true }],
        });

        var loaded = store.Load();

        Assert.True(File.Exists(Path.Combine(_dir, "cookies.json")));
        Assert.False(loaded.Enabled);
        var cookie = Assert.Single(loaded.Cookies);
        Assert.Equal(("a", "1", "h.test", "/p", true), (cookie.Name, cookie.Value, cookie.Domain, cookie.Path, cookie.Secure));
    }

    [Fact]
    public void A_missing_or_damaged_file_gives_an_empty_enabled_jar()
    {
        var store = new CookieStore(_dir);
        Assert.True(store.Load().Enabled);
        Assert.Empty(store.Load().Cookies);

        File.WriteAllText(Path.Combine(_dir, "cookies.json"), "{ not json");

        var loaded = store.Load();
        Assert.True(loaded.Enabled);
        Assert.Empty(loaded.Cookies);
    }

    // ---- Dialog view model ---------------------------------------------------------------------

    [Fact]
    public void The_dialog_lists_the_jar_and_describes_each_cookie()
    {
        var jar = new CookieJar();
        jar.Container.Add(Site, new Cookie("sid", "abc", "/", "example.test") { Secure = true, HttpOnly = true });
        jar.Container.Add(Site, new Cookie("pref", "dark") { Expires = DateTime.Now.AddDays(3) });

        var vm = new CookiesViewModel(jar, new FakeDialogService());

        Assert.True(vm.HasCookies);
        var sid = vm.Cookies.Single(c => c.Name == "sid");
        Assert.Equal("example.test", sid.Domain);
        Assert.Equal("Session", sid.ExpiresText);
        Assert.Contains("Secure", sid.FlagsText);
        Assert.Contains("HttpOnly", sid.FlagsText);
        Assert.NotEqual("Session", vm.Cookies.Single(c => c.Name == "pref").ExpiresText);
    }

    [Fact]
    public void Deleting_a_row_removes_it_from_the_jar_and_the_list()
    {
        var jar = JarWith(("a", "1"), ("b", "2"));
        var vm = new CookiesViewModel(jar, new FakeDialogService());

        vm.DeleteCommand.Execute(vm.Cookies.First(c => c.Name == "a"));

        Assert.Equal(["b"], vm.Cookies.Select(c => c.Name));
        Assert.Equal(["b"], jar.Snapshot().Select(c => c.Name));
    }

    [Fact]
    public void Clear_all_asks_first_and_does_nothing_when_declined()
    {
        var jar = JarWith(("a", "1"));
        var dialogs = new FakeDialogService { ConfirmResult = false };
        var vm = new CookiesViewModel(jar, dialogs);

        vm.ClearAllCommand.Execute(null);
        Assert.Single(jar.Snapshot());
        Assert.Single(dialogs.ConfirmMessages);

        dialogs.ConfirmResult = true;
        vm.ClearAllCommand.Execute(null);
        Assert.Empty(jar.Snapshot());
        Assert.False(vm.HasCookies);
    }

    [Fact]
    public void The_switch_writes_through_to_the_jar_and_explains_itself()
    {
        var jar = new CookieJar();
        var vm = new CookiesViewModel(jar, new FakeDialogService());
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Enabled = false;

        Assert.False(jar.Enabled);
        Assert.Contains(nameof(CookiesViewModel.StateNote), raised);
        Assert.Contains("exactly as typed", vm.StateNote);

        vm.Enabled = true;
        Assert.True(jar.Enabled);
        Assert.Contains("kept", vm.StateNote);
    }

    // ---- cURL ----------------------------------------------------------------------------------

    [Fact]
    public void Copy_as_curl_carries_the_jars_cookies_unless_the_request_types_its_own()
    {
        var request = new RequestModel { Method = "GET" };

        Assert.Contains("-b 'sid=abc; theme=dark'", CurlExporter.Build("http://h/", request, "sid=abc; theme=dark"));
        Assert.DoesNotContain("-b", CurlExporter.Build("http://h/", request, null));

        request.Headers = [new KeyValueModel { Key = "cookie", Value = "mine=1" }];
        var typed = CurlExporter.Build("http://h/", request, "sid=abc");
        Assert.DoesNotContain("-b", typed);
        Assert.Contains("-H 'cookie: mine=1'", typed);
    }

    [Fact]
    public void The_exported_cookie_option_imports_back_as_a_cookie_header()
    {
        var command = CurlExporter.Build("http://h/", new RequestModel(), "sid=abc; theme=dark");

        Assert.True(CurlParser.TryParse(command, null, out var import, out var error), error);

        var header = Assert.Single(import!.Request.Headers, h => h.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("sid=abc; theme=dark", header.Value);
    }

    // ---- On the wire ---------------------------------------------------------------------------

    /// <summary>
    /// A loopback server that answers each connection from a list of canned responses, one per
    /// request, and returns the raw text of every request it received.
    /// </summary>
    private static (string BaseUrl, Task<List<string>> Requests) Serve(params string[] responses)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var requests = Task.Run(async () =>
        {
            var seen = new List<string>();

            foreach (var response in responses)
            {
                using var client = await listener.AcceptTcpClientAsync();
                var stream = client.GetStream();
                var received = new StringBuilder();
                var buffer = new byte[4096];

                while (!received.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    var read = await stream.ReadAsync(buffer);
                    if (read == 0)
                    {
                        break;
                    }

                    received.Append(Encoding.ASCII.GetString(buffer, 0, read));
                }

                seen.Add(received.ToString());
                await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
            }

            listener.Stop();
            return seen;
        });

        return ($"http://127.0.0.1:{port}", requests);
    }

    private static string Ok(string extraHeaders = "") =>
        $"HTTP/1.1 200 OK\r\n{extraHeaders}Content-Length: 2\r\nConnection: close\r\n\r\nok";

    private static string Redirect(string location, string extraHeaders = "") =>
        $"HTTP/1.1 302 Found\r\nLocation: {location}\r\n{extraHeaders}Content-Length: 0\r\nConnection: close\r\n\r\n";

    private static RequestModel Get(string path = "/", params KeyValueModel[] headers) =>
        new() { Method = "GET", Path = path, Headers = [.. headers] };

    [Fact]
    public async Task A_cookie_set_by_one_response_is_sent_with_the_next_request()
    {
        var (baseUrl, requests) = Serve(Ok("Set-Cookie: sid=abc; Path=/\r\n"), Ok());
        var sender = new HttpRequestSender(new CookieJar());

        await sender.SendAsync(baseUrl, Get("/login"), CancellationToken.None);
        await sender.SendAsync(baseUrl, Get("/me"), CancellationToken.None);

        var seen = await requests;
        Assert.DoesNotContain("Cookie:", seen[0]);
        Assert.Contains("Cookie: sid=abc", seen[1]);
        Assert.Equal(["sid"], sender.Jar.Snapshot().Select(c => c.Name));
    }

    [Fact]
    public async Task A_cookie_set_on_a_redirect_is_kept_and_sent_to_the_redirect_target()
    {
        var (baseUrl, requests) = Serve(Redirect("/next", "Set-Cookie: sid=fromredirect; Path=/\r\n"), Ok());
        var sender = new HttpRequestSender(new CookieJar());

        var result = await sender.SendAsync(baseUrl, Get("/login"), CancellationToken.None);

        Assert.True(result.Completed, result.Error);
        var seen = await requests;
        Assert.Contains("GET /next", seen[1]);
        Assert.Contains("Cookie: sid=fromredirect", seen[1]);
    }

    [Fact]
    public async Task A_typed_cookie_header_replaces_the_jar_for_that_request_only()
    {
        var (baseUrl, requests) = Serve(Ok("Set-Cookie: sid=abc; Path=/\r\n"), Ok(), Ok());
        var sender = new HttpRequestSender(new CookieJar());

        await sender.SendAsync(baseUrl, Get("/login"), CancellationToken.None);
        await sender.SendAsync(baseUrl, Get("/typed", new KeyValueModel { Key = "Cookie", Value = "manual=1" }), CancellationToken.None);
        await sender.SendAsync(baseUrl, Get("/jar"), CancellationToken.None);

        var seen = await requests;
        Assert.Contains("Cookie: manual=1", seen[1]);
        Assert.DoesNotContain("sid=abc", seen[1]);
        Assert.Contains("Cookie: sid=abc", seen[2]);
    }

    [Fact]
    public async Task With_the_jar_off_nothing_is_stored_or_sent()
    {
        var (baseUrl, requests) = Serve(Ok("Set-Cookie: sid=abc; Path=/\r\n"), Ok());
        var sender = new HttpRequestSender(new CookieJar { Enabled = false });

        await sender.SendAsync(baseUrl, Get("/login"), CancellationToken.None);
        await sender.SendAsync(baseUrl, Get("/me"), CancellationToken.None);

        var seen = await requests;
        Assert.DoesNotContain("Cookie:", seen[1]);
        Assert.Empty(sender.Jar.Snapshot());
    }

    [Fact]
    public async Task The_jar_raises_changed_for_a_new_cookie_but_not_for_an_unchanged_one()
    {
        var (baseUrl, _) = Serve(Ok("Set-Cookie: sid=abc; Path=/\r\n"), Ok("Set-Cookie: sid=abc; Path=/\r\n"));
        var sender = new HttpRequestSender(new CookieJar());
        var changed = 0;
        sender.Jar.Changed += (_, _) => changed++;

        await sender.SendAsync(baseUrl, Get("/a"), CancellationToken.None);
        Assert.Equal(1, changed);

        await sender.SendAsync(baseUrl, Get("/b"), CancellationToken.None);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Each_set_cookie_header_is_its_own_row_in_the_response_headers()
    {
        var (baseUrl, _) = Serve(Ok("Set-Cookie: a=1; Path=/\r\nSet-Cookie: b=2; Expires=Wed, 21 Oct 2037 07:28:00 GMT; Path=/\r\n"));
        var sender = new HttpRequestSender(new CookieJar());

        var result = await sender.SendAsync(baseUrl, Get("/"), CancellationToken.None);

        var rows = result.ResponseHeaders.Where(h => h.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Value.StartsWith("a=1"));
        Assert.Contains(rows, r => r.Value.StartsWith("b=2") && r.Value.Contains("Expires=Wed, 21 Oct 2037"));
    }
}
