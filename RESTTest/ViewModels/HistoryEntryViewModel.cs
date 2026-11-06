using CommunityToolkit.Mvvm.ComponentModel;
using RESTTest.Models;
using RESTTest.Services;

namespace RESTTest.ViewModels;

/// <summary>
/// Read-only view of one history entry. History is persisted, but entries are never edited, so
/// this is a plain <see cref="ObservableObject"/> and not an <see cref="EditableViewModel"/>:
/// <see cref="HistoryViewModel"/> owns the list and tells <c>MainViewModel</c> when to save it.
/// </summary>
public sealed class HistoryEntryViewModel : ObservableObject
{
    private string? _prettyBody;

    public HistoryEntryViewModel(HistoryEntryModel model)
    {
        Model = model;
    }

    public HistoryEntryModel Model { get; }

    public string Id => Model.Id;

    public string Method => Model.Request.Method;

    public string Path => Model.Request.Path;

    public string Url => Model.Url;

    public string CollectionName => Model.CollectionName;

    public RequestModel Request => Model.Request;

    public bool BodyTruncated => Model.BodyTruncated;

    public string RawBody => Model.Body;

    public IReadOnlyList<KeyValueModel> Headers => Model.ResponseHeaders;

    public bool HasError => !Model.Completed;

    public string? Error => Model.Error;

    public int StatusCode => Model.Completed ? Model.StatusCode : 0;

    /// <summary>Short label for the list pill: "200", or "ERR" when nothing came back.</summary>
    public string StatusLabel => Model.Completed ? Model.StatusCode.ToString() : "ERR";

    public string StatusText => Model.Completed
        ? $"{Model.StatusCode} {Model.ReasonPhrase}".Trim()
        : "No response";

    public string StatusCategory => DisplayFormat.StatusCategory(StatusCode);

    public string ElapsedText => DisplayFormat.Duration(Model.ElapsedMilliseconds);

    public string SizeText => Model.Completed ? DisplayFormat.Size(Model.ContentLength) : string.Empty;

    public bool HasTests => Model.TestsTotal > 0;

    public bool AllTestsPassed => Model.TestsPassed == Model.TestsTotal;

    public string TestSummary => HasTests ? $"{Model.TestsPassed}/{Model.TestsTotal} tests passed" : string.Empty;

    /// <summary>False for text, XML and form bodies, which the viewer should not colour as JSON.</summary>
    public bool IsJsonRequestBody => Model.Request.BodyKind == BodyKind.Json;

    public bool HasRequestBody => !string.IsNullOrWhiteSpace(RequestBody);

    /// <summary>What was sent as the body, whatever its kind: text as typed, form rows as key=value lines.</summary>
    public string RequestBody => RequestBodyPreview.Describe(Model.Request);

    /// <summary>
    /// Formatted on first use, not on load: up to a hundred bodies are kept, and almost all of
    /// them are never opened.
    /// </summary>
    public string PrettyBody => _prettyBody ??= JsonText.PrettyOrOriginal(Model.Body);

    /// <summary>Time of day for today's entries, otherwise the date too.</summary>
    public string WhenText
    {
        get
        {
            var local = Model.Timestamp.ToLocalTime();
            return local.Date == DateTime.Today
                ? local.ToString("HH:mm:ss")
                : local.ToString("dd MMM HH:mm");
        }
    }

    /// <summary>"2 headers · 1 query param · Bearer auth" so the Request tab says what else was sent.</summary>
    public string RequestSummary
    {
        get
        {
            var parts = new List<string>();

            var body = RequestBodyPreview.Label(Model.Request);
            if (body.Length > 0)
            {
                parts.Add(body);
            }

            var headers = Model.Request.Headers.Count(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Key));
            if (headers > 0)
            {
                parts.Add(headers == 1 ? "1 header" : $"{headers} headers");
            }

            var query = Model.Request.QueryParams.Count(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Key));
            if (query > 0)
            {
                parts.Add(query == 1 ? "1 query param" : $"{query} query params");
            }

            if (Model.Request.Auth == AuthKind.BearerToken)
            {
                parts.Add("Bearer auth");
            }

            return parts.Count == 0 ? "No headers, parameters or auth" : string.Join(" · ", parts);
        }
    }
}
