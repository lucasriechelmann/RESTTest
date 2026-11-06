using RESTTest.Models;

namespace RESTTest.Services;

/// <summary>
/// Writes a request as a cURL command that sends what the app would send: the same method, URL,
/// headers and body, so a command can be pasted into a terminal, a ticket or a colleague's chat.
/// </summary>
/// <remarks>
/// POSIX (bash, zsh, Git Bash) quoting: every value is in single quotes, which is the one form
/// that needs no other escaping. Windows cmd.exe does not understand it; Git Bash and WSL do.
/// Checked against real bash and curl: shell metacharacters (<c>$(...)</c>, backticks) stay literal.
/// Non-ASCII text is written as is; some Windows builds of curl under Git Bash garble non-ASCII
/// arguments themselves (a hand-typed <c>--data-raw</c> with Japanese arrived as "???"), which no
/// quoting can fix.
/// </remarks>
public static class CurlExporter
{
    /// <param name="url">The full URL to call, including the query string (see <c>IHttpRequestSender.BuildUrl</c>).</param>
    public static string Build(string url, RequestModel request)
    {
        var method = string.IsNullOrWhiteSpace(request.Method) ? "GET" : request.Method.Trim().ToUpperInvariant();

        var first = method switch
        {
            // "-X HEAD" makes curl wait for a body that never comes; -I is the right spelling.
            "HEAD" => "curl -I",
            "GET" => "curl",
            _ => $"curl -X {method}",
        };

        var parts = new List<string> { $"{first} {Quote(url)}" };

        foreach (var header in Headers(request, method))
        {
            parts.Add($"-H {Quote($"{header.Key}: {header.Value}")}");
        }

        if (UsesBasicFlag(request))
        {
            parts.Add($"-u {Quote($"{request.BasicUsername}:{request.BasicPassword}")}");
        }

        parts.AddRange(Body(request, method));

        return string.Join(" \\\n  ", parts);
    }

    private static bool HasBasic(RequestModel request) =>
        request.Auth == AuthKind.Basic
        && (!string.IsNullOrEmpty(request.BasicUsername) || !string.IsNullOrEmpty(request.BasicPassword));

    /// <summary>A colon in the username would be read by curl as the start of the password.</summary>
    private static bool UsesBasicHeader(RequestModel request) =>
        HasBasic(request) && request.BasicUsername.Contains(':');

    /// <summary>
    /// <c>-u</c> reads better than a base64 header and survives a round trip through the importer.
    /// Not used when a typed Authorization header would replace the credentials anyway.
    /// </summary>
    private static bool UsesBasicFlag(RequestModel request) =>
        HasBasic(request)
        && !UsesBasicHeader(request)
        && !(request.Headers ?? []).Any(h =>
            h.Enabled && h.Key.Trim().Equals("Authorization", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<KeyValueModel> Headers(RequestModel request, string method)
    {
        var headers = (request.Headers ?? [])
            .Where(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Key))
            .Select(h => new KeyValueModel { Key = h.Key.Trim(), Value = h.Value ?? string.Empty })
            .ToList();

        bool Has(string name) => headers.Any(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase));

        // The app sends the bearer header first and lets a header of the same name replace it.
        if (request.Auth == AuthKind.BearerToken
            && !string.IsNullOrWhiteSpace(request.BearerToken)
            && !Has("Authorization"))
        {
            headers.Insert(0, new KeyValueModel { Key = "Authorization", Value = $"Bearer {request.BearerToken.Trim()}" });
        }

        // Basic goes out as -u (see Credentials) unless the username holds a colon, which -u cannot express.
        if (UsesBasicHeader(request) && !Has("Authorization"))
        {
            headers.Insert(0, new KeyValueModel
            {
                Key = "Authorization",
                Value = BasicAuth.HeaderValue(request.BasicUsername, request.BasicPassword),
            });
        }

        // A query-string key is already in the URL passed to Build.
        if (request is { Auth: AuthKind.ApiKey, ApiKeyLocation: ApiKeyLocation.Header }
            && !string.IsNullOrWhiteSpace(request.ApiKeyName)
            && !string.IsNullOrWhiteSpace(request.ApiKeyValue)
            && !Has(request.ApiKeyName.Trim()))
        {
            headers.Insert(0, new KeyValueModel { Key = request.ApiKeyName.Trim(), Value = request.ApiKeyValue.Trim() });
        }

        // The text kinds carry their own Content-Type in the app; curl needs it spelled out.
        // Forms and multipart are left to curl, which sets those (and the boundary) itself.
        if (HttpRequestSender.CarriesBody(method) && !Has("Content-Type"))
        {
            var type = request.BodyKind switch
            {
                BodyKind.Json when HasText(request) => "application/json",
                BodyKind.Text when HasText(request) => "text/plain",
                BodyKind.Xml when HasText(request) => "application/xml",
                _ => null,
            };

            if (type is not null)
            {
                headers.Add(new KeyValueModel { Key = "Content-Type", Value = type });
            }
        }

        return headers;
    }

    private static IEnumerable<string> Body(RequestModel request, string method)
    {
        if (!HttpRequestSender.CarriesBody(method))
        {
            yield break;
        }

        switch (request.BodyKind)
        {
            case BodyKind.Json:
            case BodyKind.Text:
            case BodyKind.Xml:
                // --data-raw, not -d: -d treats a leading @ as "read this file" and strips newlines.
                if (HasText(request))
                {
                    yield return $"--data-raw {Quote(request.Body)}";
                }

                break;

            case BodyKind.FormUrlEncoded:
                foreach (var field in (request.FormFields ?? []).Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Key)))
                {
                    // curl encodes the value; the name is encoded here to match what the app sends.
                    yield return $"--data-urlencode {Quote($"{Uri.EscapeDataString(field.Key.Trim())}={field.Value}")}";
                }

                break;

            case BodyKind.Multipart:
                foreach (var part in (request.MultipartParts ?? []).Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Key)))
                {
                    var name = part.Key.Trim();

                    if (!part.IsFile)
                    {
                        // --form-string: -F would read "@x" or "<x" in a text value as a file.
                        yield return $"--form-string {Quote($"{name}={part.Value}")}";
                        continue;
                    }

                    var path = (part.Value ?? string.Empty).Trim();
                    yield return $"-F {Quote($"{name}=@{FilePath(path)};type={MimeTypes.For(path)}")}";
                }

                break;
        }
    }

    private static bool HasText(RequestModel request) => !string.IsNullOrWhiteSpace(request.Body);

    /// <summary>curl splits a -F value at ';' and ',', so a path containing either (or a quote) is double-quoted.</summary>
    private static string FilePath(string path) =>
        path.IndexOfAny([';', ',', '"']) < 0
            ? path
            : $"\"{path.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    /// <summary>Single-quotes a value for a POSIX shell. A quote inside is closed, escaped, and reopened.</summary>
    private static string Quote(string value) => $"'{value.Replace("'", "'\\''")}'";
}
