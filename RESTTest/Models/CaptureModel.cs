namespace RESTTest.Models;

/// <summary>
/// Persisted form of one capture rule: after a send, read a value out of the response and keep it
/// in the collection under <see cref="Name"/> so later requests can use it ("save $.token as
/// token"). Stored on the request that produces the value.
/// </summary>
public sealed class CaptureModel
{
    public bool Enabled { get; set; } = true;

    /// <summary>The name later requests refer to. Compared without regard to case.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Which part of the response to read. Shares the assertion targets on purpose, so a capture and
    /// a test pointed at the same place read the same value; response time is not offered.
    /// </summary>
    public AssertionTarget Source { get; set; } = AssertionTarget.JsonPath;

    /// <summary>The header name or JSON path. Unused for the status code and the whole body.</summary>
    public string Selector { get; set; } = string.Empty;
}
