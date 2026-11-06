using RESTTest.Models;
using RESTTest.Services;

namespace RESTTest.Tests;

public class AssertionEvaluatorTests
{
    private static HttpExchangeResult Response(
        int status = 200,
        string body = "",
        long elapsed = 120,
        params (string Key, string Value)[] headers) => new()
    {
        Url = "http://test/x",
        Method = "GET",
        Completed = true,
        StatusCode = status,
        ReasonPhrase = "OK",
        Body = body,
        ElapsedMilliseconds = elapsed,
        ResponseHeaders = [.. headers.Select(h => new KeyValueModel { Key = h.Key, Value = h.Value })],
    };

    private static AssertionModel Check(
        AssertionTarget target,
        AssertionOperator op,
        string expected = "",
        string selector = "") => new()
    {
        Target = target,
        Operator = op,
        Expected = expected,
        Selector = selector,
    };

    private static bool Passes(AssertionModel assertion, HttpExchangeResult response) =>
        AssertionEvaluator.Evaluate(assertion, response).Passed;

    [Fact]
    public void Status_code_equals_passes_and_fails()
    {
        var check = Check(AssertionTarget.StatusCode, AssertionOperator.EqualTo, "201");

        Assert.True(Passes(check, Response(status: 201)));
        Assert.False(Passes(check, Response(status: 200)));
    }

    [Fact]
    public void Status_code_comparisons_are_numeric()
    {
        Assert.True(Passes(Check(AssertionTarget.StatusCode, AssertionOperator.LessThan, "300"), Response(status: 204)));
        Assert.True(Passes(Check(AssertionTarget.StatusCode, AssertionOperator.GreaterThan, "399"), Response(status: 404)));
        Assert.False(Passes(Check(AssertionTarget.StatusCode, AssertionOperator.LessThan, "200"), Response(status: 200)));
    }

    [Fact]
    public void A_non_numeric_expected_value_fails_a_numeric_comparison_instead_of_throwing()
    {
        var result = AssertionEvaluator.Evaluate(
            Check(AssertionTarget.StatusCode, AssertionOperator.LessThan, "fast"),
            Response());

        Assert.False(result.Passed);
    }

    [Fact]
    public void Response_time_is_compared_in_milliseconds()
    {
        var check = Check(AssertionTarget.ResponseTimeMs, AssertionOperator.LessThan, "500");

        Assert.True(Passes(check, Response(elapsed: 120)));
        Assert.False(Passes(check, Response(elapsed: 900)));
    }

    [Fact]
    public void Header_lookup_ignores_the_case_of_the_name()
    {
        var response = Response(headers: ("Content-Type", "application/json; charset=utf-8"));

        Assert.True(Passes(Check(AssertionTarget.Header, AssertionOperator.Contains, "json", "content-type"), response));
        Assert.True(Passes(Check(AssertionTarget.Header, AssertionOperator.Exists, selector: "CONTENT-TYPE"), response));
    }

    [Fact]
    public void A_missing_header_fails_exists_and_equals_but_not_the_negated_checks()
    {
        var response = Response();

        Assert.False(Passes(Check(AssertionTarget.Header, AssertionOperator.Exists, selector: "X-Nope"), response));
        Assert.False(Passes(Check(AssertionTarget.Header, AssertionOperator.EqualTo, "1", "X-Nope"), response));
        Assert.True(Passes(Check(AssertionTarget.Header, AssertionOperator.NotEqualTo, "1", "X-Nope"), response));
        Assert.True(Passes(Check(AssertionTarget.Header, AssertionOperator.NotContains, "1", "X-Nope"), response));
    }

    [Fact]
    public void A_repeated_header_is_joined_before_comparing()
    {
        var response = Response(headers: [("Set-Cookie", "a=1"), ("Set-Cookie", "b=2")]);

        Assert.True(Passes(Check(AssertionTarget.Header, AssertionOperator.Contains, "b=2", "Set-Cookie"), response));
    }

    [Fact]
    public void Body_text_contains_is_case_sensitive()
    {
        var response = Response(body: "Hello World");

        Assert.True(Passes(Check(AssertionTarget.BodyText, AssertionOperator.Contains, "World"), response));
        Assert.False(Passes(Check(AssertionTarget.BodyText, AssertionOperator.Contains, "world"), response));
        Assert.True(Passes(Check(AssertionTarget.BodyText, AssertionOperator.NotContains, "world"), response));
    }

    [Fact]
    public void Json_path_equality_treats_numbers_as_numbers()
    {
        var response = Response(body: "{\"id\": 7, \"price\": 9.50}");

        Assert.True(Passes(Check(AssertionTarget.JsonPath, AssertionOperator.EqualTo, "7", "$.id"), response));
        Assert.True(Passes(Check(AssertionTarget.JsonPath, AssertionOperator.EqualTo, "9.5", "$.price"), response));
        Assert.True(Passes(Check(AssertionTarget.JsonPath, AssertionOperator.GreaterThan, "9", "$.price"), response));
    }

    [Fact]
    public void Json_path_equality_on_text_and_booleans_uses_the_json_spelling()
    {
        var response = Response(body: "{\"name\": \"Ada\", \"ok\": true}");

        Assert.True(Passes(Check(AssertionTarget.JsonPath, AssertionOperator.EqualTo, "Ada", "$.name"), response));
        Assert.False(Passes(Check(AssertionTarget.JsonPath, AssertionOperator.EqualTo, "ada", "$.name"), response));
        Assert.True(Passes(Check(AssertionTarget.JsonPath, AssertionOperator.EqualTo, "true", "$.ok"), response));
    }

    [Fact]
    public void Json_path_exists_fails_for_a_missing_path_and_for_a_body_that_is_not_json()
    {
        Assert.True(Passes(Check(AssertionTarget.JsonPath, AssertionOperator.Exists, selector: "$.id"), Response(body: "{\"id\":1}")));
        Assert.False(Passes(Check(AssertionTarget.JsonPath, AssertionOperator.Exists, selector: "$.id"), Response(body: "{}")));
        Assert.False(Passes(Check(AssertionTarget.JsonPath, AssertionOperator.Exists, selector: "$.id"), Response(body: "<html/>")));
    }

    [Fact]
    public void A_transport_failure_fails_every_assertion_with_the_reason()
    {
        var failure = HttpExchangeResult.Failure("http://test/x", "GET", "Connection refused", 5);

        var result = AssertionEvaluator.Evaluate(
            Check(AssertionTarget.StatusCode, AssertionOperator.NotEqualTo, "500"),
            failure);

        Assert.False(result.Passed);
        Assert.Contains("Connection refused", result.Message);
    }

    [Fact]
    public void Disabled_assertions_are_skipped()
    {
        var assertions = new[]
        {
            Check(AssertionTarget.StatusCode, AssertionOperator.EqualTo, "200"),
            new AssertionModel { Enabled = false, Target = AssertionTarget.StatusCode, Expected = "999" },
        };

        var results = AssertionEvaluator.EvaluateAll(assertions, Response());

        Assert.Single(results);
        Assert.True(results[0].Passed);
    }

    [Fact]
    public void A_failure_message_says_what_was_expected_and_what_was_found()
    {
        var result = AssertionEvaluator.Evaluate(
            Check(AssertionTarget.StatusCode, AssertionOperator.EqualTo, "201"),
            Response(status: 404));

        Assert.Equal("Status code equals \"201\", but was \"404\".", result.Message);
    }

    [Fact]
    public void A_long_multiline_body_is_trimmed_in_the_message()
    {
        var body = string.Join("\n", Enumerable.Repeat("0123456789", 30));

        var result = AssertionEvaluator.Evaluate(
            Check(AssertionTarget.BodyText, AssertionOperator.EqualTo, "nope"),
            Response(body: body));

        Assert.DoesNotContain('\n', result.Message);
        Assert.True(result.Message.Length < 200);
    }
}
