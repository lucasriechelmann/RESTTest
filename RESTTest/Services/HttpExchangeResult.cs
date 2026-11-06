using RESTTest.Models;

namespace RESTTest.Services;

/// <summary>Immutable outcome of one request/response round trip, including transport failures.</summary>
public sealed record HttpExchangeResult
{
    public required string Url { get; init; }

    public required string Method { get; init; }

    /// <summary>True when a response was received at all; a 500 is still a completed exchange.</summary>
    public bool Completed { get; init; }

    public int StatusCode { get; init; }

    public string ReasonPhrase { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public long ElapsedMilliseconds { get; init; }

    public long ContentLength { get; init; }

    public string ContentType { get; init; } = string.Empty;

    public List<KeyValueModel> ResponseHeaders { get; init; } = [];

    /// <summary>Set when the request never produced a response (DNS, TLS, timeout, cancel).</summary>
    public string? Error { get; init; }

    public static HttpExchangeResult Failure(string url, string method, string error, long elapsedMs) => new()
    {
        Url = url,
        Method = method,
        Completed = false,
        Error = error,
        ElapsedMilliseconds = elapsedMs,
    };
}
