using System.Text.RegularExpressions;

namespace RESTTest.Services.Importing;

/// <summary>
/// Replaces <c>{{name}}</c> (Postman) and <c>{{ _.name }}</c> (Insomnia) with the value the source
/// file defines for it. RESTTest has no variable layer, so what cannot be resolved is left as typed
/// and counted, and the user is told once at the end.
/// </summary>
internal sealed partial class PlaceholderResolver(IReadOnlyDictionary<string, string> values)
{
    private readonly HashSet<string> _unresolved = new(StringComparer.Ordinal);

    [GeneratedRegex(@"\{\{\s*(?:_\.)?([^{}\s]+?)\s*\}\}")]
    private static partial Regex Pattern();

    /// <summary>Names that were used but have no value in the file, in first-seen order.</summary>
    public IReadOnlyCollection<string> Unresolved => _unresolved;

    public string Resolve(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{", StringComparison.Ordinal))
        {
            return text ?? string.Empty;
        }

        return Pattern().Replace(text, match =>
        {
            var name = match.Groups[1].Value;
            if (values.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value))
            {
                return value;
            }

            _unresolved.Add(name);
            return match.Value;
        });
    }

    /// <summary>The sentence for the summary, or null when everything resolved.</summary>
    public string? Describe()
    {
        if (_unresolved.Count == 0)
        {
            return null;
        }

        var names = string.Join(", ", _unresolved.Take(6).Select(n => "{{" + n + "}}"));
        var more = _unresolved.Count > 6 ? $" and {_unresolved.Count - 6} more" : string.Empty;
        return $"RESTTest has no variables, so {names}{more} had no value in the file and were left as typed. " +
               "Replace them with real values before sending.";
    }
}
