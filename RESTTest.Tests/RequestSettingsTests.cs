using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Text.Json;
using RESTTest.Models;
using RESTTest.Services;
using RESTTest.ViewModels;

namespace RESTTest.Tests;

/// <summary>Per-request timeout, redirect following and certificate checking.</summary>
public class RequestSettingsTests
{
    // ---- The saved shape -----------------------------------------------------------------------

    [Fact]
    public void A_request_saved_before_these_settings_existed_loads_with_the_old_behaviour()
    {
        var model = JsonSerializer.Deserialize<RequestModel>("""{ "Method": "GET", "Path": "/old" }""", JsonFile.Options)!;

        Assert.Equal(120, model.TimeoutSeconds);
        Assert.True(model.FollowRedirects);
        Assert.True(model.VerifySsl);
    }

    [Fact]
    public void The_settings_are_written_and_read_back()
    {
        var original = new RequestModel { TimeoutSeconds = 7, FollowRedirects = false, VerifySsl = false };

        var json = JsonSerializer.Serialize(original, JsonFile.Options);
        var back = JsonSerializer.Deserialize<RequestModel>(json, JsonFile.Options)!;

        Assert.Equal(7, back.TimeoutSeconds);
        Assert.False(back.FollowRedirects);
        Assert.False(back.VerifySsl);
    }

    // ---- The view model ------------------------------------------------------------------------

    [Fact]
    public void The_view_model_round_trips_the_settings()
    {
        var view = new RequestViewModel(new RequestModel { TimeoutSeconds = 45, FollowRedirects = false, VerifySsl = false });

        var model = view.ToModel();

        Assert.Equal(45, model.TimeoutSeconds);
        Assert.False(model.FollowRedirects);
        Assert.False(model.VerifySsl);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(90, 90)]
    [InlineData(3600, 3600)]
    [InlineData(99999, 3600)]
    public void A_timeout_outside_the_allowed_range_snaps_to_it(int typed, int kept)
    {
        var view = new RequestViewModel { TimeoutSeconds = typed };
        Assert.Equal(kept, view.TimeoutSeconds);

        // A damaged file gets the same treatment on load.
        Assert.Equal(kept, new RequestViewModel(new RequestModel { TimeoutSeconds = typed }).TimeoutSeconds);
    }

    [Fact]
    public void The_settings_tab_is_marked_only_when_something_differs_from_the_default()
    {
        var view = new RequestViewModel();
        Assert.False(view.HasCustomSettings);

        view.TimeoutSeconds = 30;
        Assert.True(view.HasCustomSettings);
        view.TimeoutSeconds = 120;
        Assert.False(view.HasCustomSettings);

        view.FollowRedirects = false;
        Assert.True(view.HasCustomSettings);
        view.FollowRedirects = true;

        view.VerifySsl = false;
        Assert.True(view.HasCustomSettings);
    }

    [Theory]
    [InlineData(0, "No time limit")]
    [InlineData(1, "after 1 second ")]
    [InlineData(30, "after 30 seconds")]
    public void The_timeout_hint_says_what_the_number_means(int seconds, string expected)
    {
        Assert.Contains(expected, new RequestViewModel { TimeoutSeconds = seconds }.TimeoutHint);
    }

    [Fact]
    public void Changing_a_setting_marks_the_collection_dirty_once_and_the_derived_flags_do_not_add_more()
    {
        var view = new RequestViewModel();
        var dirty = 0;
        view.Dirty += (_, _) => dirty++;

        view.TimeoutSeconds = 30;
        Assert.Equal(1, dirty);

        view.FollowRedirects = false;
        Assert.Equal(2, dirty);

        view.VerifySsl = false;
        Assert.Equal(3, dirty);
    }

    // ---- Rules ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(-1, 120)]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(3600, 3600)]
    [InlineData(100000, 3600)]
    public void The_sender_resolves_the_timeout(int stored, int used)
    {
        Assert.Equal(used, HttpRequestSender.ResolveTimeoutSeconds(stored));
    }

    [Theory]
    [InlineData(true, SslPolicyErrors.None, "api.example.com", true)]
    [InlineData(true, SslPolicyErrors.RemoteCertificateChainErrors, "api.example.com", false)]
    [InlineData(true, SslPolicyErrors.RemoteCertificateNameMismatch, "api.example.com", false)]
    [InlineData(true, SslPolicyErrors.RemoteCertificateChainErrors, "localhost", true)]
    [InlineData(true, SslPolicyErrors.RemoteCertificateChainErrors, "127.0.0.1", true)]
    [InlineData(true, SslPolicyErrors.RemoteCertificateChainErrors, "dev.localhost", true)]
    [InlineData(false, SslPolicyErrors.RemoteCertificateChainErrors, "api.example.com", true)]
    [InlineData(false, SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors, "api.example.com", true)]
    public void A_flawed_certificate_is_accepted_only_on_this_machine_or_when_checking_is_off(
        bool verify, SslPolicyErrors errors, string host, bool accepted)
    {
        Assert.Equal(accepted, HttpRequestSender.IsCertificateAccepted(verify, host, errors));
    }

    [Fact]
    public void The_certificate_host_can_arrive_as_the_request_message()
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, "https://bad.example.com/");

        Assert.False(HttpRequestSender.IsCertificateAccepted(true, message, SslPolicyErrors.RemoteCertificateChainErrors));
        Assert.True(HttpRequestSender.IsCertificateAccepted(false, message, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    // ---- On the wire ---------------------------------------------------------------------------

    [Fact]
    public async Task A_redirect_is_followed_by_default()
    {
        var (baseUrl, requests) = CookieJarTests.Serve(CookieJarTests.Redirect("/final"), CookieJarTests.Ok());

        var result = await new HttpRequestSender().SendAsync(baseUrl, new RequestModel { Path = "/start" }, CancellationToken.None);

        Assert.Equal(200, result.StatusCode);
        Assert.Equal(2, (await requests).Count);
    }

    [Fact]
    public async Task With_following_off_the_redirect_itself_is_the_response_and_shows_where_it_points()
    {
        // One response only: a client that followed would wait for a second connection that never comes.
        var (baseUrl, requests) = CookieJarTests.Serve(CookieJarTests.Redirect("/final"));

        var result = await new HttpRequestSender()
            .SendAsync(baseUrl, new RequestModel { Path = "/start", FollowRedirects = false }, CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(302, result.StatusCode);
        Assert.Equal("/final", result.ResponseHeaders.Single(h => h.Key == "Location").Value);
        Assert.Single(await requests);
    }

    [Fact]
    public async Task A_cookie_set_by_a_redirect_is_kept_even_when_the_redirect_is_not_followed()
    {
        var (baseUrl, _) = CookieJarTests.Serve(CookieJarTests.Redirect("/home", "Set-Cookie: sid=abc; Path=/\r\n"));
        var jar = new CookieJar();

        await new HttpRequestSender(jar)
            .SendAsync(baseUrl, new RequestModel { Path = "/login", FollowRedirects = false }, CancellationToken.None);

        Assert.Contains(jar.Snapshot(), c => c.Name == "sid" && c.Value == "abc");
    }

    [Fact]
    public async Task A_request_that_outlasts_its_timeout_fails_and_says_how_long_it_waited()
    {
        using var silent = SilentServer(out var baseUrl);

        var result = await new HttpRequestSender()
            .SendAsync(baseUrl, new RequestModel { Path = "/slow", TimeoutSeconds = 1 }, CancellationToken.None);

        Assert.False(result.Completed);
        Assert.Contains("timed out after 1 second", result.Error);
        Assert.Contains("Settings", result.Error);
        Assert.InRange(result.ElapsedMilliseconds, 800, 8000);
    }

    [Fact]
    public async Task Cancelling_is_still_reported_as_cancelled_and_not_as_a_timeout()
    {
        using var silent = SilentServer(out var baseUrl);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var result = await new HttpRequestSender()
            .SendAsync(baseUrl, new RequestModel { Path = "/slow", TimeoutSeconds = 60 }, cancel.Token);

        Assert.False(result.Completed);
        Assert.Contains("cancelled", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_timeout_of_zero_means_the_request_is_never_abandoned_by_the_sender()
    {
        using var silent = SilentServer(out var baseUrl);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));

        var result = await new HttpRequestSender()
            .SendAsync(baseUrl, new RequestModel { Path = "/slow", TimeoutSeconds = 0 }, cancel.Token);

        // Only the caller's cancel ended it: with a deadline of its own the message would say "timed out".
        Assert.Contains("cancelled", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_fast_answer_inside_the_timeout_is_unaffected()
    {
        var (baseUrl, _) = CookieJarTests.Serve(CookieJarTests.Ok());

        var result = await new HttpRequestSender()
            .SendAsync(baseUrl, new RequestModel { Path = "/", TimeoutSeconds = 5 }, CancellationToken.None);

        Assert.True(result.Completed);
        Assert.Equal(200, result.StatusCode);
    }

    /// <summary>Accepts connections and never answers, so a request to it can only end by timeout or cancel.</summary>
    private static IDisposable SilentServer(out string baseUrl)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        baseUrl = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";

        var held = new List<TcpClient>();
        _ = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    held.Add(await listener.AcceptTcpClientAsync());
                }
            }
            catch (Exception)
            {
                // Listener stopped: the test is over.
            }
        });

        return new Stopper(listener, held);
    }

    private sealed class Stopper(TcpListener listener, List<TcpClient> held) : IDisposable
    {
        public void Dispose()
        {
            listener.Stop();
            foreach (var client in held.ToArray())
            {
                client.Dispose();
            }
        }
    }
}
