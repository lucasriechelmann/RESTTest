using RESTTest.Models;

namespace RESTTest.Services;

/// <summary>
/// Reads capture values out of a response. Pure, like <see cref="AssertionEvaluator"/>, and used
/// by both a single send and the collection runner so the two cannot disagree.
/// </summary>
public static class CaptureEvaluator
{
    /// <summary>The response parts a capture can read, in the order the editor offers them.</summary>
    public static IReadOnlyList<AssertionTarget> Sources { get; } =
        [AssertionTarget.JsonPath, AssertionTarget.Header, AssertionTarget.StatusCode, AssertionTarget.BodyText];

    /// <summary>Evaluates every enabled capture that has a name, in order.</summary>
    public static IReadOnlyList<CaptureResult> EvaluateAll(IEnumerable<CaptureModel>? captures, HttpExchangeResult response) =>
        [.. (captures ?? [])
            .Where(c => c.Enabled && !string.IsNullOrWhiteSpace(c.Name))
            .Select(c => Evaluate(c, response))];

    public static CaptureResult Evaluate(CaptureModel capture, HttpExchangeResult response)
    {
        var name = capture.Name.Trim();

        if (!response.Completed)
        {
            return new CaptureResult(name, false, string.Empty, $"{name}: not captured, there was no response.");
        }

        if (!Sources.Contains(capture.Source))
        {
            return new CaptureResult(name, false, string.Empty, $"{name}: {Describe(capture)} cannot be captured.");
        }

        if (capture.Source is AssertionTarget.Header or AssertionTarget.JsonPath
            && string.IsNullOrWhiteSpace(capture.Selector))
        {
            var what = capture.Source == AssertionTarget.Header ? "header name" : "JSON path";
            return new CaptureResult(name, false, string.Empty, $"{name}: not captured, no {what} given.");
        }

        return AssertionEvaluator.TryRead(capture.Source, capture.Selector, response, out var value)
            ? new CaptureResult(name, true, value, $"{name} = {Preview(value)}")
            : new CaptureResult(name, false, string.Empty,
                $"{name}: not captured, {Describe(capture)} was not found in the response.");
    }

    /// <summary>The source in words, e.g. "JSON $.token" or "header Location".</summary>
    public static string Describe(CaptureModel capture) => capture.Source switch
    {
        AssertionTarget.JsonPath => $"JSON {capture.Selector.Trim()}".TrimEnd(),
        AssertionTarget.Header => $"header {capture.Selector.Trim()}".TrimEnd(),
        AssertionTarget.StatusCode => "the status code",
        AssertionTarget.BodyText => "the body",
        _ => capture.Source.ToString(),
    };

    /// <summary>Keeps a token or a whole body readable in a one-line note.</summary>
    public static string Preview(string value)
    {
        var flat = value.ReplaceLineEndings(" ");
        return flat.Length > 60 ? $"\"{flat[..60]}...\"" : $"\"{flat}\"";
    }
}
