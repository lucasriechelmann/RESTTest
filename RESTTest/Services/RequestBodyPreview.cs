using System.Text;
using RESTTest.Models;

namespace RESTTest.Services;

/// <summary>
/// The body of a request as readable text, for places that show what was sent (the history
/// dialog). Which kinds carry what is decided by <see cref="HttpRequestSender.BuildContent"/>;
/// this only describes it.
/// </summary>
public static class RequestBodyPreview
{
    public static string Describe(RequestModel request)
    {
        switch (request.BodyKind)
        {
            case BodyKind.Json:
            case BodyKind.Text:
            case BodyKind.Xml:
                return request.Body ?? string.Empty;

            case BodyKind.FormUrlEncoded:
                return Lines((request.FormFields ?? [])
                    .Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Key))
                    .Select(f => $"{f.Key.Trim()}={f.Value}"));

            case BodyKind.Multipart:
                // "@" is the cURL convention for "the contents of this file".
                return Lines((request.MultipartParts ?? [])
                    .Where(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Key))
                    .Select(p => p.IsFile ? $"{p.Key.Trim()}=@{p.Value}" : $"{p.Key.Trim()}={p.Value}"));

            default:
                return string.Empty;
        }
    }

    /// <summary>A short label such as "JSON body" or "Multipart form (2 parts)", or empty for no body.</summary>
    public static string Label(RequestModel request)
    {
        var sent = !string.IsNullOrWhiteSpace(Describe(request));

        return request.BodyKind switch
        {
            BodyKind.Json when sent => "JSON body",
            BodyKind.Text when sent => "Text body",
            BodyKind.Xml when sent => "XML body",
            BodyKind.FormUrlEncoded when sent => $"Form body ({Count((request.FormFields ?? []).Count(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Key)), "field")})",
            BodyKind.Multipart when sent => $"Multipart form ({Count((request.MultipartParts ?? []).Count(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Key)), "part")})",
            _ => string.Empty,
        };
    }

    private static string Count(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static string Lines(IEnumerable<string> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }
}
