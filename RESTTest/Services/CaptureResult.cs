namespace RESTTest.Services;

/// <summary>
/// The outcome of one capture rule against one response. Runtime only. <see cref="Value"/> is
/// meaningful only when <see cref="Succeeded"/>; a failed capture never overwrites a stored value.
/// </summary>
public sealed record CaptureResult(string Name, bool Succeeded, string Value, string Message);
