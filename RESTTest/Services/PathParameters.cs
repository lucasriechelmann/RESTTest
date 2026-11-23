using System.Text;
using System.Text.RegularExpressions;
using RESTTest.Models;

namespace RESTTest.Services;

/// <summary>
/// The <c>:name</c> segments of a request path, as in <c>/users/:id/posts</c>. A segment is a
/// parameter only when the whole segment is a colon followed by a name, so <c>localhost:5000</c>,
/// <c>https:</c> and <c>/a:b</c> are left alone. This is deliberately not a template language:
/// there is one syntax, it only replaces whole path segments, and the value comes from the
/// request's own Path parameters table.
/// </summary>
public static partial class PathParameters
{
    [GeneratedRegex(@"^:([A-Za-z_][A-Za-z0-9_.\-]*)$")]
    private static partial Regex SegmentPattern();

    /// <summary>The parameter names in <paramref name="path"/>, in order, each once.</summary>
    public static IReadOnlyList<string> NamesIn(string? path)
    {
        var names = new List<string>();

        foreach (var segment in Head(path).Split('/'))
        {
            var match = SegmentPattern().Match(segment);
            if (match.Success && !names.Contains(match.Groups[1].Value, StringComparer.Ordinal))
            {
                names.Add(match.Groups[1].Value);
            }
        }

        return names;
    }

    /// <summary>
    /// Replaces each <c>:name</c> segment that has a non-empty value with that value, escaped as
    /// one path segment. A parameter with no value stays as typed, so a half-filled request is
    /// visibly unfinished in the URL preview instead of silently calling a different path.
    /// </summary>
    public static string Substitute(string path, IEnumerable<KeyValueModel>? parameters)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in parameters ?? [])
        {
            if (!string.IsNullOrWhiteSpace(parameter.Key) && !string.IsNullOrEmpty(parameter.Value))
            {
                values[parameter.Key.Trim().TrimStart(':')] = parameter.Value;
            }
        }

        if (values.Count == 0 || !path.Contains(':'))
        {
            return path;
        }

        // Only the part before the query string and fragment holds path segments.
        var head = Head(path);
        var tail = path[head.Length..];

        var parts = head.Split('/');
        var builder = new StringBuilder(path.Length);
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                builder.Append('/');
            }

            var match = SegmentPattern().Match(parts[i]);
            builder.Append(match.Success && values.TryGetValue(match.Groups[1].Value, out var value)
                ? Uri.EscapeDataString(value)
                : parts[i]);
        }

        return builder.Append(tail).ToString();
    }

    private static string Head(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var end = path.IndexOfAny(['?', '#']);
        return end < 0 ? path : path[..end];
    }
}
