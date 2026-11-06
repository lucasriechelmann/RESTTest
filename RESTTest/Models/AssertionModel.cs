namespace RESTTest.Models;

/// <summary>
/// Persisted form of one check run against a response, e.g. "status code equals 200" or
/// "JSON $.data.id exists". Stored on the request so the check travels with it.
/// </summary>
public sealed class AssertionModel
{
    public bool Enabled { get; set; } = true;

    public AssertionTarget Target { get; set; } = AssertionTarget.StatusCode;

    /// <summary>The header name or JSON path. Unused by the other targets.</summary>
    public string Selector { get; set; } = string.Empty;

    public AssertionOperator Operator { get; set; } = AssertionOperator.EqualTo;

    /// <summary>Compared as text, or as a number when both sides parse as one.</summary>
    public string Expected { get; set; } = string.Empty;
}
