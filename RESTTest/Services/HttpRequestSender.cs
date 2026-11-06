using System.Diagnostics;
using System.IO;
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

        // An API key in the query string is part of the address, so it shows in the URL preview,
        // in history and in a copied cURL command exactly as it is sent.
        if (request is { Auth: AuthKind.ApiKey, ApiKeyLocation: ApiKeyLocation.Query } && HasApiKey(request))
        {
            var key = Uri.EscapeDataString(request.ApiKeyName.Trim())
                      + "=" + Uri.EscapeDataString(request.ApiKeyValue.Trim());
            query = query.Length == 0 ? key : query + "&" + key;
        }

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

    /// <summary>Whether a request with this method has its body sent at all.</summary>
    public static bool CarriesBody(string method) => BodyCapableMethods.Contains(method);

    private static void ApplyBody(HttpRequestMessage message, RequestModel request, string method)
    {
        message.Content = BuildContent(request, method);
    }

    /// <summary>
    /// The body as it goes on the wire, or null when nothing is sent: a method that carries no
    /// body, <see cref="BodyKind.None"/>, or a body with nothing in it. This is the single place
    /// that decides which body kinds exist and how each is encoded.
    /// </summary>
    /// <exception cref="FileNotFoundException">A multipart file part points at a file that is gone.</exception>
    public static HttpContent? BuildContent(RequestModel request, string method)
    {
        if (!BodyCapableMethods.Contains(method))
        {
            return null;
        }

        switch (request.BodyKind)
        {
            case BodyKind.Json:
                return TextContent(request.Body, "application/json");

            case BodyKind.Text:
                return TextContent(request.Body, "text/plain");

            case BodyKind.Xml:
                return TextContent(request.Body, "application/xml");

            case BodyKind.FormUrlEncoded:
                var fields = (request.FormFields ?? [])
                    .Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Key))
                    .Select(f => new KeyValuePair<string, string>(f.Key.Trim(), f.Value ?? string.Empty))
                    .ToList();

                return fields.Count == 0 ? null : new FormUrlEncodedContent(fields);

            case BodyKind.Multipart:
                return BuildMultipart(request.MultipartParts ?? []);

            default:
                return null;
        }
    }

    private static StringContent? TextContent(string? text, string mediaType) =>
        string.IsNullOrWhiteSpace(text) ? null : new StringContent(text, Encoding.UTF8, mediaType);

    private static MultipartFormDataContent? BuildMultipart(IEnumerable<MultipartPartModel> parts)
    {
        var active = parts.Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Key)).ToList();
        if (active.Count == 0)
        {
            return null;
        }

        var content = new MultipartFormDataContent();

        try
        {
            foreach (var part in active)
            {
                var name = part.Key.Trim();

                if (!part.IsFile)
                {
                    var text = new StringContent(part.Value ?? string.Empty, Encoding.UTF8);

                    // Browsers and cURL send a plain field with no Content-Type of its own. A typed
                    // part makes some servers treat it as a file or a structured part, not a field.
                    text.Headers.ContentType = null;
                    content.Add(text, name);
                    continue;
                }

                var path = (part.Value ?? string.Empty).Trim();
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        $"The file for multipart field '{name}' was not found: {(path.Length == 0 ? "(no file chosen)" : path)}",
                        path);
                }

                // Opened, not read: the stream is consumed when the request is sent, so a large
                // upload never sits in memory. The content owns it and closes it on dispose.
                var file = new StreamContent(File.OpenRead(path));
                file.Headers.ContentType = new MediaTypeHeaderValue(MimeTypes.For(path));
                content.Add(file, name, Path.GetFileName(path));
            }
        }
        catch
        {
            // Dispose what was opened so far, or a missing second file would leak the first one's handle.
            content.Dispose();
            throw;
        }

        return content;
    }

    /// <summary>
    /// Runs before <see cref="ApplyHeaders"/>, so a header typed on the Headers tab with the same
    /// name replaces whatever the Auth tab put here. A key that travels in the query string is not
    /// handled here: it is part of the URL, added by <see cref="BuildUrl"/>.
    /// </summary>
    private static void ApplyAuthentication(HttpRequestMessage message, RequestModel request)
    {
        switch (request.Auth)
        {
            case AuthKind.BearerToken when !string.IsNullOrWhiteSpace(request.BearerToken):
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.BearerToken.Trim());
                break;

            case AuthKind.Basic when HasBasicCredentials(request):
                message.Headers.TryAddWithoutValidation(
                    "Authorization", BasicAuth.HeaderValue(request.BasicUsername, request.BasicPassword));
                break;

            case AuthKind.ApiKey when request.ApiKeyLocation == ApiKeyLocation.Header && HasApiKey(request):
                // Without validation: key names are free text and values may hold characters the
                // typed header API rejects. An unusable name is skipped rather than failing the send.
                message.Headers.TryAddWithoutValidation(request.ApiKeyName.Trim(), request.ApiKeyValue.Trim());
                break;
        }
    }

    private static bool HasBasicCredentials(RequestModel request) =>
        !string.IsNullOrEmpty(request.BasicUsername) || !string.IsNullOrEmpty(request.BasicPassword);

    private static bool HasApiKey(RequestModel request) =>
        !string.IsNullOrWhiteSpace(request.ApiKeyName) && !string.IsNullOrWhiteSpace(request.ApiKeyValue);

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
