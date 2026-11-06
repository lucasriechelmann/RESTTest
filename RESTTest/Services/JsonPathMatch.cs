namespace RESTTest.Services;

/// <summary>One value found by a JSONPath query.</summary>
/// <param name="Path">Where it lives, normalised: <c>$['items'][0]['id']</c>.</param>
/// <param name="Json">The value as JSON text (a string keeps its quotes).</param>
public sealed record JsonPathMatch(string Path, string Json);
