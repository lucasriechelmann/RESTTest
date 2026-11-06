namespace RESTTest.Models;

/// <summary>
/// One request that was sent, with enough of its response to look at later. History is global
/// (not per collection), so the collection name is stored as text: the collection may since have
/// been renamed or deleted, and the entry should still make sense.
/// </summary>
public sealed class HistoryEntryModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset Timestamp { get; set; }

    public string CollectionName { get; set; } = string.Empty;

    /// <summary>The absolute URL that was hit (base URL + path + enabled query parameters).</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>The request exactly as it was sent, so it can be saved back into a collection.</summary>
    public RequestModel Request { get; set; } = new();

    /// <summary>False when no response arrived (DNS, TLS, timeout); see <see cref="Error"/>.</summary>
    public bool Completed { get; set; }

    public int StatusCode { get; set; }

    public string ReasonPhrase { get; set; } = string.Empty;

    public long ElapsedMilliseconds { get; set; }

    public long ContentLength { get; set; }

    public List<KeyValueModel> ResponseHeaders { get; set; } = [];

    /// <summary>The response body, cut to a fixed length so the history file stays small.</summary>
    public string Body { get; set; } = string.Empty;

    public bool BodyTruncated { get; set; }

    public string? Error { get; set; }

    /// <summary>How many assertions held when it was sent; both zero when it had none.</summary>
    public int TestsPassed { get; set; }

    public int TestsTotal { get; set; }
}
