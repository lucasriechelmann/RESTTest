namespace RESTTest.Services;

/// <summary>
/// Outcome of one assertion against one response. Runtime only: results are shown in the response
/// pane and the runner report but never saved.
/// </summary>
/// <param name="Passed">Whether the check held.</param>
/// <param name="Description">The check in words, e.g. "Status code equals 200".</param>
/// <param name="Message">Why it passed or failed, e.g. "Status code equals 200, but was 404".</param>
public sealed record AssertionResult(bool Passed, string Description, string Message);
