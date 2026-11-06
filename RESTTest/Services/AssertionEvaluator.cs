using System.Globalization;
using RESTTest.Models;

namespace RESTTest.Services;

/// <summary>
/// Pure logic for checking a response against assertions. Kept out of the ViewModels so it can be
/// tested without any UI and reused by both the single-request send and the collection runner.
/// </summary>
public static class AssertionEvaluator
{
    /// <summary>
    /// Evaluates every enabled assertion, in order. Disabled ones are skipped entirely, so the
    /// result list can be shorter than the input.
    /// </summary>
    public static IReadOnlyList<AssertionResult> EvaluateAll(
        IEnumerable<AssertionModel> assertions,
        HttpExchangeResult response) =>
        [.. assertions.Where(a => a.Enabled).Select(a => Evaluate(a, response))];

    public static AssertionResult Evaluate(AssertionModel assertion, HttpExchangeResult response)
    {
        var description = Describe(assertion);

        // A transport failure has no status, headers or body to inspect. Failing every assertion
        // (rather than skipping them) is what makes a dead server show up red in a run.
        if (!response.Completed)
        {
            return new AssertionResult(
                false,
                description,
                $"{description}, but there was no response ({response.Error ?? "request failed"}).");
        }

        var found = TryGetActual(assertion, response, out var actual);
        var passed = Compare(assertion, found, actual);

        var message = passed
            ? description
            : found
                ? $"{description}, but was {Quote(actual)}."
                : $"{description}, but {Subject(assertion)} was not found.";

        return new AssertionResult(passed, description, message);
    }

    /// <summary>The check in words, used as the row label in the Tests tab and the run report.</summary>
    public static string Describe(AssertionModel assertion)
    {
        var subject = Subject(assertion);

        return assertion.Operator switch
        {
            AssertionOperator.Exists => $"{subject} exists",
            AssertionOperator.EqualTo => $"{subject} equals {Quote(assertion.Expected)}",
            AssertionOperator.NotEqualTo => $"{subject} does not equal {Quote(assertion.Expected)}",
            AssertionOperator.Contains => $"{subject} contains {Quote(assertion.Expected)}",
            AssertionOperator.NotContains => $"{subject} does not contain {Quote(assertion.Expected)}",
            AssertionOperator.LessThan => $"{subject} is less than {Quote(assertion.Expected)}",
            AssertionOperator.GreaterThan => $"{subject} is greater than {Quote(assertion.Expected)}",
            _ => subject,
        };
    }

    private static string Subject(AssertionModel assertion) => assertion.Target switch
    {
        AssertionTarget.StatusCode => "Status code",
        AssertionTarget.ResponseTimeMs => "Response time (ms)",
        AssertionTarget.Header => $"Header {assertion.Selector}".TrimEnd(),
        AssertionTarget.BodyText => "Body",
        AssertionTarget.JsonPath => $"JSON {assertion.Selector}".TrimEnd(),
        _ => assertion.Target.ToString(),
    };

    private static bool TryGetActual(AssertionModel assertion, HttpExchangeResult response, out string actual)
    {
        actual = string.Empty;

        switch (assertion.Target)
        {
            case AssertionTarget.StatusCode:
                actual = response.StatusCode.ToString(CultureInfo.InvariantCulture);
                return true;

            case AssertionTarget.ResponseTimeMs:
                actual = response.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture);
                return true;

            case AssertionTarget.Header:
                // Header names are case-insensitive on the wire. A header sent twice is joined,
                // which is how HTTP itself defines the combined value.
                var values = response.ResponseHeaders
                    .Where(h => string.Equals(h.Key, assertion.Selector.Trim(), StringComparison.OrdinalIgnoreCase))
                    .Select(h => h.Value)
                    .ToList();
                actual = string.Join(", ", values);
                return values.Count > 0;

            case AssertionTarget.BodyText:
                actual = response.Body;
                return !string.IsNullOrEmpty(response.Body);

            case AssertionTarget.JsonPath:
                return JsonPathLite.TryGetValue(response.Body, assertion.Selector, out actual);

            default:
                return false;
        }
    }

    private static bool Compare(AssertionModel assertion, bool found, string actual)
    {
        var expected = assertion.Expected;

        switch (assertion.Operator)
        {
            case AssertionOperator.Exists:
                return found;

            // "Not" checks hold when there is nothing to compare. That is the logical reading, and
            // it lets "header X-Debug does not contain true" pass on a response with no such header.
            case AssertionOperator.NotEqualTo:
                return !found || !AreEqual(actual, expected);

            case AssertionOperator.NotContains:
                return !found || !actual.Contains(expected, StringComparison.Ordinal);

            case AssertionOperator.EqualTo:
                return found && AreEqual(actual, expected);

            case AssertionOperator.Contains:
                return found && actual.Contains(expected, StringComparison.Ordinal);

            case AssertionOperator.LessThan:
                return found && TryNumbers(actual, expected, out var a1, out var e1) && a1 < e1;

            case AssertionOperator.GreaterThan:
                return found && TryNumbers(actual, expected, out var a2, out var e2) && a2 > e2;

            default:
                return false;
        }
    }

    /// <summary>Numbers compare as numbers ("200" equals "200.0"); anything else is exact text.</summary>
    private static bool AreEqual(string actual, string expected) =>
        TryNumbers(actual, expected, out var a, out var e)
            ? a == e
            : string.Equals(actual, expected, StringComparison.Ordinal);

    private static bool TryNumbers(string actual, string expected, out double a, out double e)
    {
        e = 0;
        return double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out a)
               && double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out e);
    }

    /// <summary>Keeps messages readable when the body is huge or multi-line.</summary>
    private static string Quote(string value)
    {
        var flat = value.ReplaceLineEndings(" ");
        return flat.Length > 80 ? $"\"{flat[..80]}...\"" : $"\"{flat}\"";
    }
}
