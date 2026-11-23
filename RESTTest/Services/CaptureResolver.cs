using System.Text.Json;
using RESTTest.Models;

namespace RESTTest.Services;

/// <summary>
/// Turns a request whose fields refer to captures into the request as it will be sent: each field
/// set to "from capture" gets the collection's stored value. Runs before the sender, the URL
/// preview, history and Copy as cURL, so all of them see the same values and the sender never
/// knows captures exist. There is no text substitution: only fields that hold a capture
/// reference change, and they change as a whole.
/// </summary>
public static class CaptureResolver
{
    /// <summary>
    /// A resolved copy of <paramref name="request"/>; the input is never changed, because run
    /// snapshots are resolved again on "Run again". <paramref name="missing"/> lists every capture
    /// name a field in use needs that <paramref name="values"/> lacks (an unpicked name is "").
    /// Only fields that would be sent count: disabled rows, rows without a key, form fields of
    /// another body type and auth fields of an auth type that is not selected are ignored.
    /// </summary>
    public static RequestModel Resolve(
        RequestModel request,
        IReadOnlyDictionary<string, string> values,
        out IReadOnlyList<string> missing)
    {
        var copy = Clone(request);
        var gaps = new List<string>();

        string Lookup(string name)
        {
            var key = name.Trim();
            if (key.Length > 0 && values.TryGetValue(key, out var value))
            {
                return value;
            }

            if (!gaps.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                gaps.Add(key);
            }

            return string.Empty;
        }

        void ResolveRows(IEnumerable<KeyValueModel>? rows, bool inUse, bool checkEnabled)
        {
            foreach (var row in rows ?? [])
            {
                if (row.Capture is { } name && inUse && (!checkEnabled || row.Enabled) && !string.IsNullOrWhiteSpace(row.Key))
                {
                    row.Value = Lookup(name);
                }

                row.Capture = null;
            }
        }

        ResolveRows(copy.QueryParams, inUse: true, checkEnabled: true);
        ResolveRows(copy.Headers, inUse: true, checkEnabled: true);
        ResolveRows(copy.PathParams, inUse: true, checkEnabled: false);
        ResolveRows(copy.FormFields, inUse: copy.BodyKind == BodyKind.FormUrlEncoded, checkEnabled: true);

        if (copy is { Auth: AuthKind.BearerToken, BearerTokenCapture: { } token })
        {
            copy.BearerToken = Lookup(token);
        }

        if (copy is { Auth: AuthKind.Basic, BasicPasswordCapture: { } password })
        {
            copy.BasicPassword = Lookup(password);
        }

        if (copy is { Auth: AuthKind.ApiKey, ApiKeyValueCapture: { } key } && !string.IsNullOrWhiteSpace(copy.ApiKeyName))
        {
            copy.ApiKeyValue = Lookup(key);
        }

        copy.BearerTokenCapture = null;
        copy.BasicPasswordCapture = null;
        copy.ApiKeyValueCapture = null;

        missing = gaps;
        return copy;
    }

    /// <summary>The message shown instead of sending when <see cref="Resolve"/> reported gaps.</summary>
    public static string MissingMessage(IReadOnlyList<string> missing) => "Not sent. " + DescribeMissing(missing);

    /// <summary>What is missing and how to get it, without saying what happened as a result.</summary>
    public static string DescribeMissing(IReadOnlyList<string> missing)
    {
        var named = missing.Where(m => m.Length > 0).Select(m => $"'{m}'").ToList();
        var parts = new List<string>();

        if (named.Count == 1)
        {
            parts.Add($"Capture {named[0]} has no value yet. Send the request that captures it first (see its Captures tab), then try again.");
        }
        else if (named.Count > 1)
        {
            parts.Add($"Captures {string.Join(", ", named)} have no value yet. Send the requests that capture them first, then try again.");
        }

        if (missing.Any(m => m.Length == 0))
        {
            parts.Add("A field is set to use a capture, but no capture name is picked.");
        }

        return string.Join(" ", parts);
    }

    private static RequestModel Clone(RequestModel request) =>
        JsonSerializer.Deserialize<RequestModel>(JsonSerializer.Serialize(request, JsonFile.Options), JsonFile.Options)
        ?? new RequestModel();
}
