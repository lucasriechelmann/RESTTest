namespace RESTTest.Models;

/// <summary>
/// Persisted form of a request. Only the method and the path are stored here: the host comes
/// from whichever environment the owning collection currently has selected.
/// </summary>
public sealed class RequestModel
{
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

    public List<KeyValueModel> QueryParams { get; set; } = [];

    public List<KeyValueModel> Headers { get; set; } = [];

    /// <summary>Checks evaluated against the response after every send and every collection run.</summary>
    public List<AssertionModel> Assertions { get; set; } = [];
}
