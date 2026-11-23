namespace RESTTest.Models;

/// <summary>
/// Persisted form of a request. Only the method and the path are stored here: the host comes
/// from whichever environment the owning collection currently has selected.
/// </summary>
public sealed class RequestModel
{
    /// <summary>How long a send may take when a request does not say otherwise.</summary>
    public const int DefaultTimeoutSeconds = 120;

    /// <summary>The longest timeout the editor accepts (one hour).</summary>
    public const int MaxTimeoutSeconds = 3600;

    public string Method { get; set; } = "GET";

    /// <summary>Appended to the active environment's base URL, e.g. "/api/users/1".</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Which of the body fields below is sent, and as what. Defaults to JSON for older files.</summary>
    public BodyKind BodyKind { get; set; } = BodyKind.Json;

    /// <summary>The text for the JSON, text and XML body kinds.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Rows for the form-urlencoded body kind.</summary>
    public List<KeyValueModel> FormFields { get; set; } = [];

    /// <summary>Parts (text or file) for the multipart body kind.</summary>
    public List<MultipartPartModel> MultipartParts { get; set; } = [];

    public AuthKind Auth { get; set; } = AuthKind.None;

    public string BearerToken { get; set; } = string.Empty;

    /// <summary>Capture name the bearer token comes from instead (see <see cref="KeyValueModel.Capture"/>).</summary>
    public string? BearerTokenCapture { get; set; }

    /// <summary>Credentials for <see cref="AuthKind.Basic"/>; sent as base64 of "user:password".</summary>
    public string BasicUsername { get; set; } = string.Empty;

    public string BasicPassword { get; set; } = string.Empty;

    /// <summary>Capture name the Basic password comes from instead.</summary>
    public string? BasicPasswordCapture { get; set; }

    /// <summary>Header or query-parameter name for <see cref="AuthKind.ApiKey"/>, e.g. "X-API-Key" or "api_key".</summary>
    public string ApiKeyName { get; set; } = string.Empty;

    public string ApiKeyValue { get; set; } = string.Empty;

    /// <summary>Capture name the API key value comes from instead.</summary>
    public string? ApiKeyValueCapture { get; set; }

    public ApiKeyLocation ApiKeyLocation { get; set; } = ApiKeyLocation.Header;

    /// <summary>
    /// Seconds before the send is abandoned, covering the whole exchange including the body. Zero
    /// means no limit. Files saved before this setting existed have no value and get the default.
    /// </summary>
    public int TimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>Whether a 3xx response is followed to its Location. When off, the 3xx itself is the response.</summary>
    public bool FollowRedirects { get; set; } = true;

    /// <summary>
    /// When off, any TLS certificate is accepted (expired, self-signed, wrong host). When on, only
    /// loopback hosts get that leniency, as before this setting existed.
    /// </summary>
    public bool VerifySsl { get; set; } = true;

    /// <summary>
    /// Values for the <c>:name</c> segments of <see cref="Path"/>, keyed by name (without the colon).
    /// An empty value leaves the segment as typed. <see cref="KeyValueModel.Enabled"/> is unused.
    /// </summary>
    public List<KeyValueModel> PathParams { get; set; } = [];

    public List<KeyValueModel> QueryParams { get; set; } = [];

    public List<KeyValueModel> Headers { get; set; } = [];

    /// <summary>Checks evaluated against the response after every send and every collection run.</summary>
    public List<AssertionModel> Assertions { get; set; } = [];

    /// <summary>Values read from the response after every send and run, and stored in the collection.</summary>
    public List<CaptureModel> Captures { get; set; } = [];
}
