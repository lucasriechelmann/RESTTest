using System.Text;

namespace RESTTest.Services;

/// <summary>The outcome of running a JSONPath query against a body.</summary>
public sealed class JsonPathResult
{
    /// <summary>False when the body is not JSON or the path could not be understood; see <see cref="Error"/>.</summary>
    public bool Ok { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<JsonPathMatch> Matches { get; init; } = [];

    /// <summary>
    /// True when the path can only ever select one value (names and indexes, no wildcard, filter,
    /// slice, union or recursion): its result is shown as that value, not wrapped in an array.
    /// </summary>
    public bool IsDefinite { get; init; }

    /// <summary>True when more matches existed than the cap allows.</summary>
    public bool Truncated { get; init; }

    /// <summary>"3 matches", "1 match" or "No matches".</summary>
    public string Summary =>
        !Ok ? Error ?? string.Empty
        : Matches.Count == 0 ? "No matches"
        : (Matches.Count == 1 ? "1 match" : $"{Matches.Count} matches") + (Truncated ? " (more not shown)" : string.Empty);

    /// <summary>
    /// The matches as text to display: the value itself for a definite path with one match, otherwise
    /// a JSON array of the values, one per line. Empty when nothing matched.
    /// </summary>
    public string ToDisplayJson()
    {
        if (Matches.Count == 0)
        {
            return string.Empty;
        }

        if (IsDefinite && Matches.Count == 1)
        {
            return Matches[0].Json;
        }

        var builder = new StringBuilder("[");
        for (var i = 0; i < Matches.Count; i++)
        {
            builder.Append(i == 0 ? "\n" : ",\n").Append(Matches[i].Json);
        }

        return builder.Append("\n]").ToString();
    }
}
