using RESTTest.Models;
using RESTTest.Services;
using RESTTest.ViewModels;

namespace RESTTest.Tests;

/// <summary>Basic and API-key authentication: what is sent, what is exported, what is imported, what is saved.</summary>
public class AuthTests
{
    private static readonly HttpRequestSender Sender = new();

    private static RequestModel Basic(string user = "ada", string password = "secret") => new()
    {
        Method = "GET",
        Path = "/items",
        Auth = AuthKind.Basic,
        BasicUsername = user,
        BasicPassword = password,
    };

    private static RequestModel ApiKey(ApiKeyLocation where, string name = "X-API-Key", string value = "k123") => new()
    {
        Method = "GET",
        Path = "/items",
        Auth = AuthKind.ApiKey,
        ApiKeyName = name,
        ApiKeyValue = value,
        ApiKeyLocation = where,
    };

    // ---- BasicAuth helper --------------------------------------------------------------------

    [Fact]
    public void Basic_credentials_encode_as_base64_of_user_colon_password()
    {
        Assert.Equal("Basic YWRhOnNlY3JldA==", BasicAuth.HeaderValue("ada", "secret"));
    }

    [Fact]
    public void Basic_credentials_are_encoded_as_utf8()
    {
        Assert.True(BasicAuth.TryParse(BasicAuth.HeaderValue("zoë", "pässwörd"), out var user, out var password));
        Assert.Equal("zoë", user);
        Assert.Equal("pässwörd", password);
    }

    [Theory]
    [InlineData("Bearer abc")]
    [InlineData("Basic")]
    [InlineData("Basic not*base64")]
    [InlineData("Basic YWRh")] // "ada": decodes, but has no colon to split on
    public void Values_that_are_not_usable_basic_credentials_are_rejected(string header)
    {
        Assert.False(BasicAuth.TryParse(header, out _, out _));
    }

    [Fact]
    public void Only_the_first_colon_splits_user_from_password_and_the_scheme_is_case_insensitive()
    {
        var value = "basic " + Convert.ToBase64String("ada:se:cret"u8.ToArray());

        Assert.True(BasicAuth.TryParse(value, out var user, out var password));
        Assert.Equal("ada", user);
        Assert.Equal("se:cret", password);
    }

    // ---- URL -----------------------------------------------------------------------------------

    [Fact]
    public void A_query_api_key_is_part_of_the_url_after_the_query_rows()
    {
        var request = ApiKey(ApiKeyLocation.Query, "api_key", "a b/1");
        request.QueryParams = [new KeyValueModel { Key = "page", Value = "2" }];

        Assert.Equal("http://h/items?page=2&api_key=a%20b%2F1", Sender.BuildUrl("http://h", request));
    }

    [Fact]
    public void A_query_api_key_starts_the_query_string_when_there_are_no_rows()
    {
        Assert.Equal("http://h/items?api_key=k123", Sender.BuildUrl("http://h", ApiKey(ApiKeyLocation.Query, "api_key")));
    }

    [Fact]
    public void A_header_api_key_and_other_auth_kinds_leave_the_url_alone()
    {
        Assert.Equal("http://h/items", Sender.BuildUrl("http://h", ApiKey(ApiKeyLocation.Header)));
        Assert.Equal("http://h/items", Sender.BuildUrl("http://h", Basic()));
    }

    [Fact]
    public void An_api_key_with_no_name_or_no_value_adds_nothing()
    {
        Assert.Equal("http://h/items", Sender.BuildUrl("http://h", ApiKey(ApiKeyLocation.Query, name: " ")));
        Assert.Equal("http://h/items", Sender.BuildUrl("http://h", ApiKey(ApiKeyLocation.Query, value: "")));
    }

    [Fact]
    public void A_stored_query_key_is_ignored_once_the_type_is_switched_away_from_api_key()
    {
        var request = ApiKey(ApiKeyLocation.Query, "api_key");
        request.Auth = AuthKind.None;

        Assert.Equal("http://h/items", Sender.BuildUrl("http://h", request));
    }

    // ---- On the wire ---------------------------------------------------------------------------

    [Fact]
    public async Task Basic_auth_sends_the_authorization_header()
    {
        var (baseUrl, captured) = await BodyBuildingTests.StartCapturingServer();

        var result = await Sender.SendAsync(baseUrl, Basic(), CancellationToken.None);

        Assert.True(result.Completed, result.Error);
        Assert.Contains("Authorization: Basic YWRhOnNlY3JldA==", await captured);
    }

    [Fact]
    public async Task Basic_auth_with_an_empty_password_still_sends_the_colon()
    {
        var (baseUrl, captured) = await BodyBuildingTests.StartCapturingServer();

        await Sender.SendAsync(baseUrl, Basic(password: ""), CancellationToken.None);

        Assert.Contains("Authorization: Basic " + Convert.ToBase64String("ada:"u8.ToArray()), await captured);
    }

    [Fact]
    public async Task Basic_auth_with_no_credentials_sends_no_header()
    {
        var (baseUrl, captured) = await BodyBuildingTests.StartCapturingServer();

        await Sender.SendAsync(baseUrl, Basic(user: "", password: ""), CancellationToken.None);

        Assert.DoesNotContain("Authorization:", await captured, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_typed_authorization_header_replaces_the_basic_credentials()
    {
        var (baseUrl, captured) = await BodyBuildingTests.StartCapturingServer();
        var request = Basic();
        request.Headers = [new KeyValueModel { Key = "Authorization", Value = "Custom xyz" }];

        await Sender.SendAsync(baseUrl, request, CancellationToken.None);

        var raw = await captured;
        Assert.Contains("Authorization: Custom xyz", raw);
        Assert.DoesNotContain("Basic", raw);
    }

    [Fact]
    public async Task A_header_api_key_is_sent_under_its_own_name()
    {
        var (baseUrl, captured) = await BodyBuildingTests.StartCapturingServer();

        var result = await Sender.SendAsync(baseUrl, ApiKey(ApiKeyLocation.Header), CancellationToken.None);

        Assert.True(result.Completed, result.Error);
        var raw = await captured;
        Assert.Contains("X-API-Key: k123", raw);
        Assert.StartsWith("GET /items HTTP/1.1", raw);
    }

    [Fact]
    public async Task A_query_api_key_is_on_the_request_line_and_not_in_a_header()
    {
        var (baseUrl, captured) = await BodyBuildingTests.StartCapturingServer();

        var result = await Sender.SendAsync(baseUrl, ApiKey(ApiKeyLocation.Query, "api_key"), CancellationToken.None);

        Assert.True(result.Completed, result.Error);
        Assert.Contains("api_key=k123", result.Url);
        var raw = await captured;
        Assert.StartsWith("GET /items?api_key=k123 HTTP/1.1", raw);
        Assert.DoesNotContain("X-API-Key", raw);
    }

    [Fact]
    public async Task A_header_api_key_with_an_unusable_name_is_skipped_rather_than_failing_the_send()
    {
        var (baseUrl, captured) = await BodyBuildingTests.StartCapturingServer();

        var result = await Sender.SendAsync(baseUrl, ApiKey(ApiKeyLocation.Header, "bad name"), CancellationToken.None);

        Assert.True(result.Completed, result.Error);
        Assert.DoesNotContain("k123", await captured);
    }

    // ---- cURL export ---------------------------------------------------------------------------

    [Fact]
    public void Basic_auth_is_exported_as_dash_u()
    {
        var command = CurlExporter.Build("http://h/items", Basic("ada", "it's"));

        Assert.Contains("-u 'ada:it'\\''s'", command);
        Assert.DoesNotContain("Authorization", command);
    }

    [Fact]
    public void A_colon_in_the_username_falls_back_to_the_header_form()
    {
        var command = CurlExporter.Build("http://h/items", Basic("a:b", "pw"));

        Assert.Contains("-H 'Authorization: " + BasicAuth.HeaderValue("a:b", "pw") + "'", command);
        Assert.DoesNotContain("-u ", command);
    }

    [Fact]
    public void A_typed_authorization_header_wins_over_basic_in_the_export_as_it_does_on_send()
    {
        var request = Basic();
        request.Headers = [new KeyValueModel { Key = "authorization", Value = "Custom xyz" }];

        var command = CurlExporter.Build("http://h/items", request);

        Assert.Contains("-H 'authorization: Custom xyz'", command);
        Assert.DoesNotContain("-u ", command);
    }

    [Fact]
    public void A_header_api_key_is_exported_as_a_header()
    {
        var command = CurlExporter.Build("http://h/items", ApiKey(ApiKeyLocation.Header));

        Assert.Contains("-H 'X-API-Key: k123'", command);
    }

    [Fact]
    public void A_query_api_key_is_exported_in_the_url_only()
    {
        var request = ApiKey(ApiKeyLocation.Query, "api_key");
        var command = CurlExporter.Build(Sender.BuildUrl("http://h", request), request);

        Assert.Contains("'http://h/items?api_key=k123'", command);
        Assert.DoesNotContain("-H", command);
    }

    // ---- Round trip ----------------------------------------------------------------------------

    [Theory]
    [InlineData("ada", "secret")]
    [InlineData("ada", "")]
    [InlineData("ada", "p:w'd $x")]
    [InlineData("zoë", "pässwörd")]
    public void Basic_auth_survives_export_and_import(string user, string password)
    {
        var original = Basic(user, password);

        Assert.True(CurlParser.TryParse(CurlExporter.Build("http://h/items", original), null, out var import, out var error), error);

        Assert.Equal(AuthKind.Basic, import!.Request.Auth);
        Assert.Equal(user, import.Request.BasicUsername);
        Assert.Equal(password, import.Request.BasicPassword);
    }

    [Fact]
    public void Basic_auth_with_a_colon_in_the_username_survives_through_the_header_form()
    {
        Assert.True(CurlParser.TryParse(CurlExporter.Build("http://h/", Basic("a:b", "pw")), null, out var import, out _));

        // "a:b:pw" splits at the first colon on the way back, which is what every server does too.
        Assert.Equal(AuthKind.Basic, import!.Request.Auth);
        Assert.Equal("a", import.Request.BasicUsername);
        Assert.Equal("b:pw", import.Request.BasicPassword);
    }

    // ---- ViewModel and persistence -------------------------------------------------------------

    [Fact]
    public void Auth_settings_survive_the_view_model_round_trip()
    {
        var model = ApiKey(ApiKeyLocation.Query, "api_key", "k9");
        model.BasicUsername = "ada";
        model.BasicPassword = "secret";

        var back = new RequestViewModel(model).ToModel();

        Assert.Equal(AuthKind.ApiKey, back.Auth);
        Assert.Equal("api_key", back.ApiKeyName);
        Assert.Equal("k9", back.ApiKeyValue);
        Assert.Equal(ApiKeyLocation.Query, back.ApiKeyLocation);
        Assert.Equal("ada", back.BasicUsername);
        Assert.Equal("secret", back.BasicPassword);
    }

    [Fact]
    public void Files_saved_before_these_fields_existed_load_with_empty_credentials()
    {
        var model = System.Text.Json.JsonSerializer.Deserialize<RequestModel>(
            "{\"Method\":\"GET\",\"Path\":\"/x\",\"Auth\":\"BearerToken\",\"BearerToken\":\"t\"}", JsonFile.Options)!;

        var vm = new RequestViewModel(model);

        Assert.Equal(AuthKind.BearerToken, vm.Auth);
        Assert.Equal(string.Empty, vm.BasicUsername);
        Assert.Equal(string.Empty, vm.ApiKeyName);
        Assert.Equal(ApiKeyLocation.Header, vm.ApiKeyPlacement);
    }

    [Fact]
    public void The_new_kinds_and_the_key_location_are_saved_by_name()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(ApiKey(ApiKeyLocation.Query), JsonFile.Options);

        Assert.Contains("\"Auth\": \"ApiKey\"", json);
        Assert.Contains("\"ApiKeyLocation\": \"Query\"", json);
    }

    [Fact]
    public void Switching_the_type_notifies_the_view_about_every_form_flag()
    {
        var vm = new RequestViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Auth = AuthKind.Basic;

        Assert.Contains(nameof(RequestViewModel.IsBasicAuth), raised);
        Assert.Contains(nameof(RequestViewModel.IsBearerAuth), raised);
        Assert.Contains(nameof(RequestViewModel.IsApiKeyAuth), raised);
        Assert.True(vm.IsBasicAuth);
        Assert.False(vm.IsApiKeyAuth);
    }

    [Fact]
    public void The_api_key_hint_follows_the_name_and_the_location()
    {
        var vm = new RequestViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        Assert.Equal("Sent as: <name>: <value>", vm.ApiKeyHint);

        vm.ApiKeyName = " X-Token ";
        Assert.Equal("Sent as: X-Token: <value>", vm.ApiKeyHint);

        vm.ApiKeyPlacement = ApiKeyLocation.Query;
        Assert.Equal("Sent as: ?X-Token=<value> on the URL", vm.ApiKeyHint);
        Assert.Equal(2, raised.Count(p => p == nameof(RequestViewModel.ApiKeyHint)));
    }

    [Fact]
    public void Editing_credentials_marks_the_collection_dirty_but_switching_the_form_flags_alone_does_not()
    {
        var vm = new RequestViewModel();
        var dirty = 0;
        vm.Dirty += (_, _) => dirty++;

        vm.BasicUsername = "ada";
        vm.ApiKeyValue = "k";
        vm.ApiKeyPlacement = ApiKeyLocation.Query;
        Assert.Equal(3, dirty);

        dirty = 0;
        vm.Auth = AuthKind.Basic; // Auth itself is persisted: exactly one Dirty, not one per flag.
        Assert.Equal(1, dirty);
    }

    [Fact]
    public void The_auth_picker_offers_all_four_kinds_in_order()
    {
        Assert.Equal(
            [AuthKind.None, AuthKind.BearerToken, AuthKind.Basic, AuthKind.ApiKey],
            RequestViewModel.AuthKinds);
    }

    [Fact]
    public void History_names_the_new_auth_kinds()
    {
        string Summary(AuthKind kind)
        {
            var entry = new HistoryEntryViewModel(new HistoryEntryModel
            {
                Request = new RequestModel { Auth = kind },
                Timestamp = DateTime.UtcNow,
            });
            return entry.RequestSummary;
        }

        Assert.Equal("Basic auth", Summary(AuthKind.Basic));
        Assert.Equal("API key", Summary(AuthKind.ApiKey));
    }
}
