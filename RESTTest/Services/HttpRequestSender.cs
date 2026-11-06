using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using RESTTest.Models;

namespace RESTTest.Services;

/// <summary>Turns a <see cref="RequestModel"/> plus the active base URL into a real HTTP call.</summary>
public sealed class HttpRequestSender : IHttpRequestSender
{
    /// <summary>Headers that belong on the message content rather than on the request itself.</summary>
    private static readonly HashSet<string> ContentHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Allow", "Content-Disposition", "Content-Encoding", "Content-Language", "Content-Length",
        "Content-Location", "Content-MD5", "Content-Range", "Content-Type", "Expires", "Last-Modified",
    };

    /// <summary>Methods for which a typed body is sent.</summary>
    private static readonly HashSet<string> BodyCapableMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH", "DELETE",
    };

    // A single client for the process lifetime: a client per request exhausts sockets.
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            // Cookies off: an API tester should send exactly the headers shown in the UI.
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            SslOptions = new SslClientAuthenticationOptions
            {
                // Self-signed certificates are normal on a dev box, so trust loopback only.
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                    errors == SslPolicyErrors.None
                    || (sender is string host && IsLoopbackHost(host))
                    || (sender is HttpRequestMessage { RequestUri: { } uri } && IsLoopbackHost(uri.Host)),
            },
        };

        // Per-request deadlines come from a CancellationTokenSource instead, so the Cancel
        // button works and the timeout can differ per call.
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host is "127.0.0.1" or "::1" or "[::1]"
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);

    public string BuildUrl(string? baseUrl, RequestModel request)
    {
        var path = (request.Path ?? string.Empty).Trim();

        // A fully qualified path wins, so a one-off request can reach outside the collection.
        string combined;
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            combined = path;
        }
        else
        {
            // Explicit composition: new Uri(base, "/x") silently drops the base's own path.
            var root = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            combined = path.Length == 0
                ? root
                : root + (path.StartsWith('/') ? string.Empty : "/") + path;
        }

        var query = BuildQueryString(request.QueryParams);
        if (query.Length == 0)
        {
            return combined;
        }

        var separator = combined.Contains('?') ? '&' : '?';
        return combined + separator + query;
    }

    private static string BuildQueryString(IEnumerable<KeyValueModel>? parameters)
    {
        if (parameters is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var parameter in parameters)
        {
            if (!parameter.Enabled || string.IsNullOrWhiteSpace(parameter.Key))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(parameter.Key.Trim()))
                   .Append('=')
                   .Append(Uri.EscapeDataString(parameter.Value ?? string.Empty));
        }

        return builder.ToString();
    }

    public async Task<HttpExchangeResult> SendAsync(
        string? baseUrl,
        RequestModel request,
        CancellationToken cancellationToken)
    {
        var url = BuildUrl(baseUrl, request);
        var method = string.IsNullOrWhiteSpace(request.Method) ? "GET" : request.Method.ToUpperInvariant();
        var stopwatch = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(url))
        {
            return HttpExchangeResult.Failure(url, method,
                "No URL to call. Add a base URL to the collection, or type a path.", 0);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return HttpExchangeResult.Failure(url, method,
                $"'{url}' is not a valid http or https URL.", 0);
        }

        try
        {
            using var message = new HttpRequestMessage(new HttpMethod(method), uri);
            ApplyBody(message, request, method);
            ApplyAuthentication(message, request);
            ApplyHeaders(message, request);

            using var response = await Client
                .SendAsync(message, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            return new HttpExchangeResult
            {
                Url = url,
                Method = method,
                Completed = true,
                StatusCode = (int)response.StatusCode,
                ReasonPhrase = response.ReasonPhrase ?? response.StatusCode.ToString(),
                Body = body,
                ElapsedMilliseconds = stopwatch.ElapsedMilliseconds,
                ContentLength = response.Content.Headers.ContentLength ?? Encoding.UTF8.GetByteCount(body),
                ContentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty,
                ResponseHeaders = CollectHeaders(response),
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return HttpExchangeResult.Failure(url, method, "Request cancelled.", stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return HttpExchangeResult.Failure(url, method, "The request timed out.", stopwatch.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            // The inner exception holds the actionable detail (refused, DNS, TLS).
            var detail = ex.InnerException?.Message ?? ex.Message;
            return HttpExchangeResult.Failure(url, method, detail, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return HttpExchangeResult.Failure(url, method, ex.Message, stopwatch.ElapsedMilliseconds);
        }
    }

    private static void ApplyBody(HttpRequestMessage message, RequestModel request, string method)
    {
        if (string.IsNullOrWhiteSpace(request.Body) || !BodyCapableMethods.Contains(method))
        {
            return;
        }

        message.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
    }

    private static void ApplyAuthentication(HttpRequestMessage message, RequestModel request)
    {
        if (request.Auth != AuthKind.BearerToken || string.IsNullOrWhiteSpace(request.BearerToken))
        {
            return;
        }

        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.BearerToken.Trim());
    }

    private static void ApplyHeaders(HttpRequestMessage message, RequestModel request)
    {
        foreach (var header in request.Headers ?? [])
        {
            if (!header.Enabled || string.IsNullOrWhiteSpace(header.Key))
            {
                continue;
            }

            var name = header.Key.Trim();
            var value = header.Value ?? string.Empty;

            if (ContentHeaders.Contains(name))
            {
                // Content headers are illegal on HttpRequestMessage.Headers and throw if set there.
                message.Content ??= new StringContent(string.Empty);
                message.Content.Headers.Remove(name);
                message.Content.Headers.TryAddWithoutValidation(name, value);
            }
            else
            {
                message.Headers.Remove(name);
                message.Headers.TryAddWithoutValidation(name, value);
            }
        }
    }

    private static List<KeyValueModel> CollectHeaders(HttpResponseMessage response)
    {
        var headers = new List<KeyValueModel>();

        foreach (var header in response.Headers)
        {
            headers.Add(new KeyValueModel { Key = header.Key, Value = string.Join(", ", header.Value) });
        }

        foreach (var header in response.Content.Headers)
        {
            headers.Add(new KeyValueModel { Key = header.Key, Value = string.Join(", ", header.Value) });
        }

        return [.. headers.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase)];
    }
}
