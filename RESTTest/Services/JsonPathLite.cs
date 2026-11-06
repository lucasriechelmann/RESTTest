using System.Globalization;
using System.Text.Json;

namespace RESTTest.Services;

/// <summary>
/// A deliberately small JSON path: <c>$.data.items[0].id</c> (the leading <c>$</c> is optional) and
/// <c>['quoted key']</c> for names containing dots. No wildcards, filters or recursion; assertions
/// only need to point at one value.
/// </summary>
public static class JsonPathLite
{
    private static readonly JsonDocumentOptions ReaderOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Splits a path into property names (string) and array indexes (int).</summary>
    public static bool TryParse(string? path, out List<object> segments)
    {
        segments = [];

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var text = path.Trim();
        if (text[0] == '$')
        {
            text = text[1..];
        }

        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];

            if (c == '.')
            {
                i++;
                continue;
            }

            if (c == '[')
            {
                var end = text.IndexOf(']', i);
                if (end < 0)
                {
                    return false;
                }

                var inner = text[(i + 1)..end].Trim();

                if (int.TryParse(inner, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                {
                    segments.Add(index);
                }
                else if (inner.Length >= 2
                         && (inner[0] == '\'' || inner[0] == '"')
                         && inner[^1] == inner[0])
                {
                    segments.Add(inner[1..^1]);
                }
                else
                {
                    return false;
                }

                i = end + 1;
                continue;
            }

            var start = i;
            while (i < text.Length && text[i] != '.' && text[i] != '[')
            {
                i++;
            }

            segments.Add(text[start..i]);
        }

        return true;
    }

    /// <summary>
    /// Looks the path up in <paramref name="json"/>. Strings come back unquoted; numbers, booleans,
    /// null, objects and arrays come back as their raw JSON text. False when the body is not JSON,
    /// the path is malformed, or nothing lives at that path.
    /// </summary>
    public static bool TryGetValue(string? json, string? path, out string value)
    {
        value = string.Empty;

        if (string.IsNullOrWhiteSpace(json) || !TryParse(path, out var segments))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json, ReaderOptions);
            var current = document.RootElement;

            foreach (var segment in segments)
            {
                switch (segment)
                {
                    case string name when current.ValueKind == JsonValueKind.Object
                                          && current.TryGetProperty(name, out var property):
                        current = property;
                        break;

                    case int index when current.ValueKind == JsonValueKind.Array
                                        && index < current.GetArrayLength():
                        current = current[index];
                        break;

                    default:
                        return false;
                }
            }

            value = current.ValueKind == JsonValueKind.String
                ? current.GetString() ?? string.Empty
                : current.GetRawText();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
