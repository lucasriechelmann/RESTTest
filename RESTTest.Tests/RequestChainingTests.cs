using System.Text.Json;
using RESTTest.Models;
using RESTTest.Services;
using RESTTest.ViewModels;

namespace RESTTest.Tests;

/// <summary>
/// Request chaining: captures read values out of a response, the collection stores them, and
/// fields set to "from capture" (plus ":name" path parameters) use them on the next send or run.
/// </summary>
public class RequestChainingTests
{
    private static readonly Dictionary<string, string> NoValues = [];

    private static HttpExchangeResult Response(string body = "", int status = 200, params (string Key, string Value)[] headers) => new()
    {
        Url = "http://api.test/x",
        Method = "GET",
        Completed = true,
        StatusCode = status,
        ReasonPhrase = "OK",
        Body = body,
        ResponseHeaders = [.. headers.Select(h => new KeyValueModel { Key = h.Key, Value = h.Value })],
    };

    private static CaptureModel Capture(string name, AssertionTarget source, string selector = "") =>
        new() { Name = name, Source = source, Selector = selector };

    private static Dictionary<string, string> Values(params (string Name, string Value)[] values) =>
        values.ToDictionary(v => v.Name, v => v.Value, StringComparer.OrdinalIgnoreCase);

    // ---- Path parameters -------------------------------------------------------------------

    [Fact]
    public void Path_parameter_names_are_whole_segments_that_start_with_a_colon()
    {
        Assert.Equal(["id", "postId"], PathParameters.NamesIn("/users/:id/posts/:postId?x=:nope"));
        Assert.Empty(PathParameters.NamesIn("http://localhost:5000/a:b/c:"));
        Assert.Equal(["id"], PathParameters.NamesIn("/:id/:id"));
    }

    [Fact]
    public void Path_parameters_are_substituted_escaped_and_an_empty_one_is_left_as_typed()
    {
        var parameters = new List<KeyValueModel>
        {
            new() { Key = "id", Value = "a b/c" },
            new() { Key = "other", Value = string.Empty },
        };

        Assert.Equal("/users/a%20b%2Fc/:other?q=:id",
            PathParameters.Substitute("/users/:id/:other?q=:id", parameters));
    }

    [Fact]
    public void BuildUrl_fills_path_parameters_but_not_the_port_of_an_absolute_url()
    {
        var sender = new HttpRequestSender();
        var request = new RequestModel
        {
            Path = "http://localhost:5000/users/:id",
            PathParams = [new() { Key = "id", Value = "42" }],
        };

        Assert.Equal("http://localhost:5000/users/42", sender.BuildUrl(null, request));

        request.Path = "/users/:id";
        Assert.Equal("https://api.test/v1/users/42", sender.BuildUrl("https://api.test/v1", request));
    }

    [Fact]
    public void Path_parameter_rows_follow_the_path_and_keep_values_by_name()
    {
        var request = new RequestViewModel { Path = "/users/:id" };
        request.PathParams[0].Value = "7";

        request.Path = "/users/:id/posts/:post";
        Assert.Equal(["id", "post"], request.PathParams.Select(p => p.Key));
        Assert.Equal("7", request.PathParams[0].Value);

        // Typing over the name parks the row; typing it back restores its value.
        request.Path = "/users/:i";
        request.Path = "/users/:id";
        Assert.Equal("7", Assert.Single(request.PathParams).Value);

        request.Path = "/users";
        Assert.False(request.HasPathParams);
    }

    [Fact]
    public void An_older_request_with_a_colon_segment_gets_its_row_on_load_without_being_dirty()
    {
        var request = new RequestViewModel(new RequestModel { Path = "/users/:id" });
        var dirty = false;
        request.Dirty += (_, _) => dirty = true;

        Assert.Equal("id", Assert.Single(request.PathParams).Key);
        Assert.False(dirty);
        Assert.Equal("id", Assert.Single(request.ToModel().PathParams).Key);
    }

    // ---- Capturing -------------------------------------------------------------------------

    [Fact]
    public void Captures_read_json_headers_status_and_body()
    {
        var response = Response("""{"token":"abc","user":{"id":5}}""", 201, ("Location", "/users/5"));

        var results = CaptureEvaluator.EvaluateAll(
            [
                Capture("token", AssertionTarget.JsonPath, "$.token"),
                Capture("id", AssertionTarget.JsonPath, "$.user.id"),
                Capture("where", AssertionTarget.Header, "location"),
                Capture("status", AssertionTarget.StatusCode),
                Capture("all", AssertionTarget.BodyText),
            ],
            response);

        Assert.All(results, r => Assert.True(r.Succeeded, r.Message));
        Assert.Equal(["abc", "5", "/users/5", "201", """{"token":"abc","user":{"id":5}}"""], results.Select(r => r.Value));
    }

    [Fact]
    public void A_capture_that_finds_nothing_says_why_and_disabled_or_unnamed_rules_are_skipped()
    {
        var results = CaptureEvaluator.EvaluateAll(
            [
                Capture("token", AssertionTarget.JsonPath, "$.missing"),
                Capture("noPath", AssertionTarget.JsonPath),
                new CaptureModel { Name = "off", Enabled = false, Selector = "$.a" },
                Capture("  ", AssertionTarget.StatusCode),
            ],
            Response("""{"a":1}"""));

        Assert.Equal(2, results.Count);
        Assert.False(results[0].Succeeded);
        Assert.Contains("$.missing was not found", results[0].Message);
        Assert.Contains("no JSON path given", results[1].Message);
    }

    [Fact]
    public void Nothing_is_captured_from_a_failed_exchange()
    {
        var result = CaptureEvaluator.Evaluate(
            Capture("status", AssertionTarget.StatusCode),
            HttpExchangeResult.Failure("http://x", "GET", "refused", 0));

        Assert.False(result.Succeeded);
        Assert.Contains("no response", result.Message);
    }

    // ---- Resolving -------------------------------------------------------------------------

    [Fact]
    public void Fields_from_a_capture_get_the_stored_value_and_typed_fields_are_untouched()
    {
        var request = new RequestModel
        {
            Path = "/users/:id",
            Auth = AuthKind.BearerToken,
            BearerToken = "typed token",
            BearerTokenCapture = "token",
            PathParams = [new() { Key = "id", Capture = "userId" }],
            QueryParams = [new() { Key = "q", Value = "typed" }, new() { Key = "page", Capture = "PAGE" }],
            Headers = [new() { Key = "X-Trace", Capture = "trace" }],
        };

        var resolved = CaptureResolver.Resolve(
            request, Values(("token", "T"), ("userId", "5"), ("page", "2"), ("trace", "t-1")), out var missing);

        Assert.Empty(missing);
        Assert.Equal("T", resolved.BearerToken);
        Assert.Equal("5", resolved.PathParams[0].Value);
        Assert.Equal(["typed", "2"], resolved.QueryParams.Select(p => p.Value));
        Assert.Equal("t-1", resolved.Headers[0].Value);

        // The resolved copy has no references left, and the input is unchanged (runs re-resolve it).
        Assert.Null(resolved.BearerTokenCapture);
        Assert.All(resolved.QueryParams, p => Assert.Null(p.Capture));
        Assert.Equal("typed token", request.BearerToken);
        Assert.Equal("PAGE", request.QueryParams[1].Capture);
        Assert.Equal(string.Empty, request.QueryParams[1].Value);
    }

    [Fact]
    public void Only_fields_that_would_be_sent_count_as_missing()
    {
        var request = new RequestModel
        {
            Auth = AuthKind.None,
            BearerTokenCapture = "unusedToken",
            BodyKind = BodyKind.Json,
            FormFields = [new() { Key = "f", Capture = "unusedForm" }],
            QueryParams =
            [
                new() { Key = "off", Enabled = false, Capture = "disabled" },
                new() { Key = "", Capture = "noKey" },
                new() { Key = "need", Capture = "needed" },
                new() { Key = "pick", Capture = "" },
            ],
        };

        CaptureResolver.Resolve(request, NoValues, out var missing);

        Assert.Equal(["needed", ""], missing);
        var message = CaptureResolver.MissingMessage(missing);
        Assert.StartsWith("Not sent.", message);
        Assert.Contains("'needed'", message);
        Assert.Contains("no capture name is picked", message);
    }

    [Fact]
    public void A_capture_reference_survives_saving_and_loading_and_typed_values_save_none()
    {
        var request = new RequestViewModel { Auth = AuthKind.ApiKey, ApiKeyName = "X-Key", ApiKeyValue = "typed" };
        request.ApiKeyValueUsesCapture = true;
        request.ApiKeyValueCapture = " key ";
        request.Headers.Add(new KeyValueViewModel { Key = "A", Value = "1" });

        var json = JsonSerializer.Serialize(request.ToModel(), JsonFile.Options);
        var loaded = new RequestViewModel(JsonSerializer.Deserialize<RequestModel>(json, JsonFile.Options)!);

        Assert.True(loaded.ApiKeyValueUsesCapture);
        Assert.Equal("key", loaded.ApiKeyValueCapture);
        Assert.Equal("typed", loaded.ApiKeyValue);
        Assert.False(loaded.Headers[0].UsesCapture);
        Assert.Null(loaded.ToModel().Headers[0].Capture);
        Assert.DoesNotContain("\"Capture\"", json);
    }

    // ---- Storing -------------------------------------------------------------------------

    [Fact]
    public void The_collection_keeps_successful_captures_replacing_older_values_by_name()
    {
        var collection = new CollectionViewModel();
        var dirty = 0;
        collection.Dirty += (_, _) => dirty++;

        collection.StoreCaptures([new CaptureResult("token", true, "old", "")], "Login");
        collection.StoreCaptures(
            [new CaptureResult("TOKEN", true, "new", ""), new CaptureResult("id", false, "", "not found")],
            "Login again");

        var stored = Assert.Single(collection.CapturedValues);
        Assert.Equal("new", stored.Value);
        Assert.Equal("Login again", stored.Source);
        Assert.Equal("new", collection.CaptureValueMap()["token"]);
        Assert.Equal(2, dirty);

        // The same value again is not an edit, so a repeated send does not rewrite the file.
        collection.StoreCaptures([new CaptureResult("token", true, "new", "")], "Login again");
        Assert.Equal(2, dirty);
    }

    [Fact]
    public void Captured_values_are_saved_with_the_collection_and_cleared_on_request()
    {
        var collection = new CollectionViewModel();
        collection.StoreCaptures([new CaptureResult("token", true, "T", "")], "Login");

        var loaded = CollectionViewModel.FromModel(
            JsonSerializer.Deserialize<CollectionModel>(
                JsonSerializer.Serialize(collection.ToModel(), JsonFile.Options), JsonFile.Options)!);

        Assert.Equal("T", Assert.Single(loaded.CapturedValues).Value);
        Assert.Contains("token", loaded.CaptureNames);

        loaded.ClearCapturedValuesCommand.Execute(null);
        Assert.False(loaded.HasCapturedValues);
    }

    [Fact]
    public void Capture_names_offered_to_pickers_come_from_rules_and_stored_values()
    {
        var node = NodeViewModel.CreateRequest("Login");
        var collection = new CollectionViewModel();
        collection.Items.Add(node);

        node.Request!.Captures.Add(new CaptureViewModel { Name = "token" });
        collection.StoreCaptures([new CaptureResult("userId", true, "5", "")], "Other");

        Assert.Equal(["token", "userId"], collection.CaptureNames);

        node.Request.Captures[0].Name = "session";
        Assert.Equal(["session", "userId"], collection.CaptureNames);
    }

    // ---- Running -------------------------------------------------------------------------

    private sealed class RecordingSender : IHttpRequestSender
    {
        public List<RequestModel> Sent { get; } = [];

        public Func<RequestModel, HttpExchangeResult> Respond { get; set; } = _ => Response();

        public string BuildUrl(string? baseUrl, RequestModel request) => (baseUrl ?? string.Empty) + request.Path;

        public Task<HttpExchangeResult> SendAsync(string? baseUrl, RequestModel request, CancellationToken cancellationToken)
        {
            Sent.Add(request);
            return Task.FromResult(Respond(request));
        }
    }

    [Fact]
    public async Task A_run_passes_a_captured_value_to_the_requests_after_it()
    {
        var sender = new RecordingSender
        {
            Respond = request => request.Path == "/login" ? Response("""{"token":"T1"}""") : Response(),
        };

        var login = new RunItemViewModel("Login", new RequestModel
        {
            Path = "/login",
            Captures = [Capture("token", AssertionTarget.JsonPath, "$.token")],
        });
        var me = new RunItemViewModel("Me", new RequestModel
        {
            Path = "/me",
            Auth = AuthKind.BearerToken,
            BearerTokenCapture = "token",
        });

        var stored = new List<(string Name, string Source)>();
        var runner = new RunnerViewModel(sender, "http://api.test", "Run", [login, me])
        {
            CaptureValuesProvider = () => Values(("token", "stale")),
            CapturesStoredHandler = (captures, source) => stored.AddRange(captures.Select(c => (c.Name, source))),
        };

        await runner.RunAsync();

        Assert.Equal("T1", sender.Sent[1].BearerToken);
        Assert.Equal([("token", "Login")], stored);
        Assert.Equal(RunItemState.Unchecked, me.State);
    }

    [Fact]
    public async Task A_request_whose_capture_has_no_value_fails_in_the_run_without_being_sent()
    {
        var sender = new RecordingSender();
        var item = new RunItemViewModel("Me", new RequestModel
        {
            Path = "/me",
            Headers = [new() { Key = "X-Token", Capture = "token" }],
        });

        await new RunnerViewModel(sender, "http://api.test", "Run", [item]).RunAsync();

        Assert.Empty(sender.Sent);
        Assert.Equal(RunItemState.Failed, item.State);
        Assert.Contains("'token' has no value yet", Assert.Single(item.Failures));
    }

    [Fact]
    public async Task A_capture_that_read_nothing_fails_the_run_row()
    {
        var item = new RunItemViewModel("Login", new RequestModel
        {
            Path = "/login",
            Captures = [Capture("token", AssertionTarget.JsonPath, "$.token")],
        });

        await new RunnerViewModel(new RecordingSender(), "http://api.test", "Run", [item]).RunAsync();

        Assert.Equal(RunItemState.Failed, item.State);
        Assert.Contains("$.token was not found", Assert.Single(item.Failures));
    }

    [Fact]
    public void The_response_note_names_what_was_captured_and_what_was_not()
    {
        var response = new ResponseViewModel();
        response.Load(Response("{}"));
        response.SetCaptures(
            [new CaptureResult("token", true, "T", "token = \"T\""), new CaptureResult("id", false, "", "id: not captured, JSON $.id was not found in the response.")]);

        Assert.Equal("Captured token. Capture id: not captured, JSON $.id was not found in the response.", response.CaptureNote);
        Assert.True(response.CaptureNoteIsError);

        response.Load(Response("{}"));
        Assert.Equal(string.Empty, response.CaptureNote);
    }
}
