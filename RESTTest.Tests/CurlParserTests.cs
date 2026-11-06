using RESTTest.Models;
using RESTTest.Services;

namespace RESTTest.Tests;

public class CurlParserTests
{
    private static CurlImport Parse(string command, string? baseUrl = null)
    {
        Assert.True(CurlParser.TryParse(command, baseUrl, out var import, out var error), error);
        return import!;
    }

    private static string Fail(string command)
    {
        Assert.False(CurlParser.TryParse(command, null, out var import, out var error));
        Assert.Null(import);
        return error!;
    }

    private static string? Header(CurlImport import, string name) =>
        import.Request.Headers.LastOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    // ---- URL and the collection's base URL -------------------------------------------------

    [Fact]
    public void A_url_on_the_active_base_url_becomes_a_relative_path_with_query_rows()
    {
        var import = Parse("curl 'https://api.example.com/v1/users?page=2&q=a+b%26c'", "https://api.example.com/v1");

        Assert.Equal("/users", import.Request.Path);
        Assert.False(import.KeptAbsoluteUrl);
        Assert.Equal(["page", "q"], import.Request.QueryParams.Select(p => p.Key));
        Assert.Equal(["2", "a b&c"], import.Request.QueryParams.Select(p => p.Value));
    }

    [Theory]
    [InlineData("https://api.example.com", "https://api.example.com/")]
    [InlineData("https://api.example.com/", "https://api.example.com")]
    [InlineData("HTTPS://API.EXAMPLE.COM", "https://api.example.com/users")]
    public void The_base_url_match_ignores_case_and_a_trailing_slash(string baseUrl, string url)
    {
        var import = Parse($"curl {url}", baseUrl);

        Assert.False(import.KeptAbsoluteUrl);
        Assert.StartsWith(string.Empty, import.Request.Path);
        Assert.DoesNotContain("://", import.Request.Path);
    }

    [Fact]
    public void A_url_equal_to_the_base_url_has_an_empty_path()
    {
        Assert.Equal(string.Empty, Parse("curl https://api.example.com", "https://api.example.com").Request.Path);
    }

    [Fact]
    public void A_different_host_keeps_the_whole_url_in_the_path_and_says_so()
    {
        var import = Parse("curl https://other.test/things", "https://api.example.com");

        Assert.True(import.KeptAbsoluteUrl);
        Assert.Equal("https://other.test/things", import.Request.Path);
    }

    [Fact]
    public void A_host_that_only_starts_like_the_base_url_is_not_mistaken_for_it()
    {
        var import = Parse("curl https://api.example.com.evil.test/users", "https://api.example.com");

        Assert.True(import.KeptAbsoluteUrl);
        Assert.StartsWith("https://api.example.com.evil.test", import.Request.Path);
    }

    [Fact]
    public void Without_a_base_url_the_full_url_is_kept()
    {
        var import = Parse("curl https://api.example.com/users");

        Assert.True(import.KeptAbsoluteUrl);
        Assert.Equal("https://api.example.com/users", import.Request.Path);
    }

    [Fact]
    public void A_url_without_a_scheme_is_http_like_curl_assumes()
    {
        Assert.Equal("http://localhost:5000/x", Parse("curl localhost:5000/x").Request.Path);
    }

    [Fact]
    public void A_bare_path_stays_relative()
    {
        var import = Parse("curl /users?x=1");

        Assert.Equal("/users", import.Request.Path);
        Assert.False(import.KeptAbsoluteUrl);
        Assert.Equal("x", Assert.Single(import.Request.QueryParams).Key);
    }

    [Fact]
    public void A_fragment_is_dropped()
    {
        Assert.Equal("/a", Parse("curl 'http://h/a#section'", "http://h").Request.Path);
    }

    [Theory]
    [InlineData("curl ftp://host/file")]
    [InlineData("curl http://")]
    public void A_url_that_is_not_http_or_https_is_refused(string command)
    {
        Assert.Contains("not a valid http or https URL", Fail(command));
    }

    [Fact]
    public void The_url_can_come_from_the_url_option_and_more_than_one_url_warns()
    {
        var import = Parse("curl --url http://h/one http://h/two");

        Assert.Equal("http://h/one", import.Request.Path);
        Assert.Contains(import.Warnings, w => w.Contains("more than one URL"));
    }

    // ---- Method ----------------------------------------------------------------------------

    [Theory]
    [InlineData("curl http://h/", "GET")]
    [InlineData("curl -X put http://h/", "PUT")]
    [InlineData("curl -XPATCH http://h/", "PATCH")]
    [InlineData("curl --request DELETE http://h/", "DELETE")]
    [InlineData("curl -I http://h/", "HEAD")]
    [InlineData("curl --head http://h/", "HEAD")]
    [InlineData("curl -d a=1 http://h/", "POST")]
    [InlineData("curl -F a=1 http://h/", "POST")]
    [InlineData("curl -G -d a=1 http://h/", "GET")]
    [InlineData("curl -X GET -d a=1 http://h/", "GET")]
    public void The_method_follows_curls_rules(string command, string expected)
    {
        Assert.Equal(expected, Parse(command).Request.Method);
    }

    [Fact]
    public void Get_mode_turns_the_data_into_query_parameters()
    {
        var import = Parse("curl -G http://h/s -d q=hello -d 'page=2'");

        Assert.Equal(BodyKind.None, import.Request.BodyKind);
        Assert.Equal(["q", "page"], import.Request.QueryParams.Select(p => p.Key));
    }

    // ---- Headers, auth and friends ---------------------------------------------------------

    [Fact]
    public void Headers_are_read_in_order_and_repeats_are_kept()
    {
        var import = Parse("curl http://h/ -H 'Accept: application/json' -H 'X-Id: 1' --header 'X-Id: 2'");

        Assert.Equal(["Accept", "X-Id", "X-Id"], import.Request.Headers.Select(h => h.Key));
        Assert.Equal(["application/json", "1", "2"], import.Request.Headers.Select(h => h.Value));
        Assert.All(import.Request.Headers, h => Assert.True(h.Enabled));
    }

    [Fact]
    public void A_header_value_may_contain_colons()
    {
        Assert.Equal("a:b:c", Header(Parse("curl http://h/ -H 'X-Time: a:b:c'"), "X-Time"));
    }

    [Fact]
    public void A_bearer_authorization_header_becomes_the_bearer_auth_type()
    {
        var import = Parse("curl http://h/ -H 'Authorization: Bearer abc.def'");

        Assert.Equal(AuthKind.BearerToken, import.Request.Auth);
        Assert.Equal("abc.def", import.Request.BearerToken);
        Assert.Empty(import.Request.Headers);
    }

    [Fact]
    public void Other_authorization_headers_are_kept_as_headers()
    {
        var import = Parse("curl http://h/ -H 'Authorization: Token xyz'");

        Assert.Equal(AuthKind.None, import.Request.Auth);
        Assert.Equal("Token xyz", Header(import, "Authorization"));
    }

    [Fact]
    public void The_oauth2_bearer_option_sets_bearer_auth()
    {
        var import = Parse("curl http://h/ --oauth2-bearer tok");

        Assert.Equal(AuthKind.BearerToken, import.Request.Auth);
        Assert.Equal("tok", import.Request.BearerToken);
    }

    [Fact]
    public void Basic_auth_becomes_the_header_curl_would_send()
    {
        var import = Parse("curl -u ada:secret http://h/");

        Assert.Equal("Basic YWRhOnNlY3JldA==", Header(import, "Authorization"));
    }

    [Fact]
    public void Basic_auth_with_no_password_warns_and_uses_an_empty_one()
    {
        var import = Parse("curl -u ada http://h/");

        Assert.Equal("Basic " + Convert.ToBase64String("ada:"u8.ToArray()), Header(import, "Authorization"));
        Assert.Contains(import.Warnings, w => w.Contains("-u"));
    }

    [Fact]
    public void Cookies_user_agent_and_referer_become_headers()
    {
        var import = Parse("curl http://h/ -b 'a=1; b=2' -A 'my-agent/1.0' -e 'http://ref/'");

        Assert.Equal("a=1; b=2", Header(import, "Cookie"));
        Assert.Equal("my-agent/1.0", Header(import, "User-Agent"));
        Assert.Equal("http://ref/", Header(import, "Referer"));
    }

    [Fact]
    public void A_cookie_file_is_skipped_with_a_warning()
    {
        var import = Parse("curl http://h/ -b cookies.txt");

        Assert.Null(Header(import, "Cookie"));
        Assert.Contains(import.Warnings, w => w.Contains("cookies"));
    }

    [Fact]
    public void A_header_without_a_colon_warns_but_an_empty_header_removal_does_not()
    {
        var import = Parse("curl http://h/ -H 'nonsense' -H 'Accept:' -H 'X-Empty;'");

        Assert.Empty(import.Request.Headers);
        Assert.Single(import.Warnings, w => w.Contains("nonsense"));
    }

    // ---- Bodies ----------------------------------------------------------------------------

    [Fact]
    public void A_json_content_type_makes_a_json_body_and_the_redundant_header_goes()
    {
        var import = Parse("curl http://h/ -H 'Content-Type: application/json; charset=utf-8' -d '{\"a\":1}'");

        Assert.Equal(BodyKind.Json, import.Request.BodyKind);
        Assert.Equal("{\"a\":1}", import.Request.Body);
        Assert.Null(Header(import, "Content-Type"));
    }

    [Fact]
    public void A_json_flavoured_type_keeps_its_header_so_it_is_still_sent()
    {
        var import = Parse("curl http://h/ -H 'Content-Type: application/vnd.api+json' -d '{}'");

        Assert.Equal(BodyKind.Json, import.Request.BodyKind);
        Assert.Equal("application/vnd.api+json", Header(import, "Content-Type"));
    }

    [Fact]
    public void An_xml_content_type_makes_an_xml_body()
    {
        var import = Parse("curl http://h/ -H 'Content-Type: application/xml' -d '<a/>'");

        Assert.Equal(BodyKind.Xml, import.Request.BodyKind);
        Assert.Equal("<a/>", import.Request.Body);
        Assert.Null(Header(import, "Content-Type"));
    }

    [Fact]
    public void A_plain_text_content_type_makes_a_text_body()
    {
        var import = Parse("curl http://h/ -H 'content-type: text/plain' --data-raw 'hello'");

        Assert.Equal(BodyKind.Text, import.Request.BodyKind);
        Assert.Null(Header(import, "Content-Type"));
    }

    [Fact]
    public void Some_other_content_type_sends_the_text_and_keeps_the_header()
    {
        var import = Parse("curl http://h/ -H 'Content-Type: text/csv' -d 'a,b'");

        Assert.Equal(BodyKind.Text, import.Request.BodyKind);
        Assert.Equal("text/csv", Header(import, "Content-Type"));
    }

    [Fact]
    public void Data_with_no_content_type_that_looks_like_form_pairs_becomes_a_form()
    {
        var import = Parse("curl http://h/ -d 'user=ada+lovelace&q=a%26b'");

        Assert.Equal(BodyKind.FormUrlEncoded, import.Request.BodyKind);
        Assert.Equal(["user", "q"], import.Request.FormFields.Select(f => f.Key));
        Assert.Equal(["ada lovelace", "a&b"], import.Request.FormFields.Select(f => f.Value));
    }

    [Fact]
    public void Json_data_with_no_content_type_is_taken_as_json_and_the_guess_is_reported()
    {
        var import = Parse("curl http://h/ -d '{\"a\": [1, 2]}'");

        Assert.Equal(BodyKind.Json, import.Request.BodyKind);
        Assert.Contains(import.Warnings, w => w.Contains("looks like JSON"));
    }

    [Fact]
    public void An_explicit_form_content_type_is_a_form_and_the_header_goes()
    {
        var import = Parse("curl http://h/ -H 'Content-Type: application/x-www-form-urlencoded' -d 'a=1'");

        Assert.Equal(BodyKind.FormUrlEncoded, import.Request.BodyKind);
        Assert.Null(Header(import, "Content-Type"));
    }

    [Fact]
    public void Several_data_options_are_joined_with_ampersands()
    {
        var import = Parse("curl http://h/ -d a=1 -d b=2 --data c=3");

        Assert.Equal(["a", "b", "c"], import.Request.FormFields.Select(f => f.Key));
    }

    [Fact]
    public void Data_raw_keeps_a_leading_at_sign_as_text()
    {
        var import = Parse("curl http://h/ -H 'Content-Type: text/plain' --data-raw '@not-a-file'");

        Assert.Equal("@not-a-file", import.Request.Body);
        Assert.Empty(import.Warnings);
    }

    [Fact]
    public void Data_read_from_a_file_is_skipped_with_a_warning_and_no_body()
    {
        var import = Parse("curl http://h/ -d @payload.json");

        Assert.Equal(BodyKind.None, import.Request.BodyKind);
        Assert.Contains(import.Warnings, w => w.Contains("payload.json"));

        // curl would POST here, so the method stays POST and the warning explains the missing body.
        Assert.Equal("POST", import.Request.Method);
    }

    [Fact]
    public void Data_urlencode_adds_form_fields_with_their_content_unencoded()
    {
        var import = Parse("curl http://h/ --data-urlencode 'name=John Doe' --data-urlencode 'q=a&b=c'");

        Assert.Equal(BodyKind.FormUrlEncoded, import.Request.BodyKind);
        Assert.Equal(["name", "q"], import.Request.FormFields.Select(f => f.Key));
        Assert.Equal(["John Doe", "a&b=c"], import.Request.FormFields.Select(f => f.Value));
    }

    [Fact]
    public void Data_urlencode_without_a_name_or_from_a_file_is_skipped_with_warnings()
    {
        var import = Parse("curl http://h/ --data-urlencode 'just content' --data-urlencode 'f@file.txt' --data-urlencode 'ok=1'");

        Assert.Equal("ok", Assert.Single(import.Request.FormFields).Key);
        Assert.Equal(2, import.Warnings.Count);
    }

    [Fact]
    public void Form_options_make_a_multipart_body_with_text_and_files()
    {
        var import = Parse("curl http://h/ -F 'title=My report' -F 'doc=@/tmp/a.pdf;type=application/pdf' --form 'n=1;type=text/plain'");

        Assert.Equal(BodyKind.Multipart, import.Request.BodyKind);
        var parts = import.Request.MultipartParts;
        Assert.Equal(3, parts.Count);
        Assert.False(parts[0].IsFile);
        Assert.Equal("My report", parts[0].Value);
        Assert.True(parts[1].IsFile);
        Assert.Equal("/tmp/a.pdf", parts[1].Value);
        Assert.Equal("1", parts[2].Value);
    }

    [Fact]
    public void A_quoted_file_path_may_contain_a_semicolon()
    {
        var import = Parse("curl http://h/ -F 'f=@\"/tmp/a;b.txt\";type=text/plain'");

        Assert.Equal("/tmp/a;b.txt", import.Request.MultipartParts[0].Value);
    }

    [Fact]
    public void Form_string_keeps_a_leading_at_sign_as_text()
    {
        var part = Assert.Single(Parse("curl http://h/ --form-string 'k=@literal'").Request.MultipartParts);

        Assert.False(part.IsFile);
        Assert.Equal("@literal", part.Value);
    }

    [Fact]
    public void A_form_wins_over_data_as_in_curl()
    {
        var import = Parse("curl http://h/ -d a=1 -F b=2");

        Assert.Equal(BodyKind.Multipart, import.Request.BodyKind);
        Assert.Equal("b", Assert.Single(import.Request.MultipartParts).Key);
    }

    [Fact]
    public void The_json_option_sets_a_json_body_and_an_accept_header()
    {
        var import = Parse("curl http://h/ --json '{\"a\":1}'");

        Assert.Equal("POST", import.Request.Method);
        Assert.Equal(BodyKind.Json, import.Request.BodyKind);
        Assert.Equal("{\"a\":1}", import.Request.Body);
        Assert.Equal("application/json", Header(import, "Accept"));
    }

    // ---- Shell quoting ---------------------------------------------------------------------

    [Fact]
    public void Double_quotes_allow_escaped_quotes_and_backslashes()
    {
        var import = Parse("curl http://h/ -H \"Content-Type: application/json\" -d \"{\\\"a\\\":\\\"b\\\\c\\\"}\"");

        Assert.Equal("{\"a\":\"b\\c\"}", import.Request.Body);
    }

    [Fact]
    public void A_dollar_quoted_string_understands_newlines_and_unicode_escapes()
    {
        var import = Parse("curl http://h/ -H 'Content-Type: application/json' --data-raw $'{\\n  \"name\": \"caf\\u00e9 \\x41\"\\n}'");

        Assert.Equal("{\n  \"name\": \"café A\"\n}", import.Request.Body);
    }

    [Fact]
    public void A_dollar_quoted_string_can_hold_an_escaped_single_quote()
    {
        var import = Parse("curl http://h/ -H 'Content-Type: text/plain' --data-raw $'it\\'s'");

        Assert.Equal("it's", import.Request.Body);
    }

    [Fact]
    public void Backslash_newline_joins_lines_in_both_unix_and_windows_line_endings()
    {
        var unix = Parse("curl http://h/ \\\n  -H 'A: 1' \\\n  -H 'B: 2'");
        var windows = Parse("curl http://h/ \\\r\n  -H 'A: 1' \\\r\n  -H 'B: 2'");

        Assert.Equal(["A", "B"], unix.Request.Headers.Select(h => h.Key));
        Assert.Equal(["A", "B"], windows.Request.Headers.Select(h => h.Key));
    }

    [Fact]
    public void Adjacent_quoted_pieces_form_one_word()
    {
        Assert.Equal("a b", Header(Parse("curl http://h/ -H'X: a'' b'"), "X"));
    }

    [Fact]
    public void A_single_quote_inside_a_body_written_the_posix_way_is_read_back()
    {
        var import = Parse("curl http://h/ -H 'Content-Type: text/plain' --data-raw 'it'\\''s'");

        Assert.Equal("it's", import.Request.Body);
    }

    [Fact]
    public void An_empty_data_option_is_a_post_with_no_body_as_in_curl()
    {
        var import = Parse("curl http://h/ -d ''");

        Assert.Equal("POST", import.Request.Method);
        Assert.Equal(BodyKind.None, import.Request.BodyKind);
        Assert.Empty(import.Request.FormFields);
    }

    [Theory]
    [InlineData("curl 'http://h/")]
    [InlineData("curl \"http://h/")]
    [InlineData("curl $'http://h/")]
    public void An_unclosed_quote_is_reported(string command)
    {
        Assert.Contains("never closed", Fail(command));
    }

    // ---- Option handling -------------------------------------------------------------------

    [Fact]
    public void Common_flags_are_ignored_quietly_alone_or_clustered()
    {
        var import = Parse("curl -sSL --compressed -v -i http://h/x");

        Assert.Empty(import.Warnings);
        Assert.Equal("http://h/x", import.Request.Path);
    }

    [Fact]
    public void A_cluster_can_end_in_an_option_that_takes_a_value()
    {
        var import = Parse("curl -sX POST http://h/");

        Assert.Equal("POST", import.Request.Method);
        Assert.Equal("http://h/", import.Request.Path);
    }

    [Fact]
    public void The_insecure_flag_is_ignored_with_an_explanation()
    {
        var import = Parse("curl -k https://h/");

        Assert.Contains(import.Warnings, w => w.Contains("localhost"));
    }

    [Theory]
    [InlineData("curl http://h/ -o out.txt")]
    [InlineData("curl http://h/ -O")]
    [InlineData("curl http://h/ --proxy http://p:8080")]
    [InlineData("curl http://h/ -T file.bin")]
    public void Options_that_change_meaning_are_reported(string command)
    {
        Assert.NotEmpty(Parse(command).Warnings);
    }

    [Fact]
    public void An_ignored_option_that_takes_a_value_does_not_swallow_the_url_or_leak_as_one()
    {
        var import = Parse("curl --max-time 30 --connect-timeout 5 http://h/ok");

        Assert.Equal("http://h/ok", import.Request.Path);
        Assert.Empty(import.Warnings);
    }

    [Fact]
    public void An_unknown_option_is_reported_by_name()
    {
        var import = Parse("curl --frobnicate http://h/");

        Assert.Contains(import.Warnings, w => w.Contains("--frobnicate"));
    }

    [Fact]
    public void An_option_missing_its_value_is_an_error_not_a_silent_skip()
    {
        Assert.Contains("needs a value", Fail("curl http://h/ -H"));
        Assert.Contains("needs a value", Fail("curl http://h/ --request"));
    }

    [Fact]
    public void Everything_after_a_double_dash_is_a_url()
    {
        Assert.Equal("http://h/-x", Parse("curl -- http://h/-x").Request.Path);
    }

    // ---- What counts as a command ----------------------------------------------------------

    [Theory]
    [InlineData("curl http://h/")]
    [InlineData("curl.exe http://h/")]
    [InlineData("CURL http://h/")]
    [InlineData("/usr/bin/curl http://h/")]
    [InlineData("C:\\Windows\\System32\\curl.exe http://h/")]
    [InlineData("$ curl http://h/")]
    [InlineData("  \n  curl http://h/  \n")]
    public void The_command_name_and_a_pasted_prompt_are_accepted(string command)
    {
        Assert.Equal("http://h/", Parse(command).Request.Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Nothing_pasted_is_reported(string? command)
    {
        Assert.False(CurlParser.TryParse(command, null, out _, out var error));
        Assert.Contains("Paste a cURL command", error);
    }

    [Fact]
    public void Text_that_is_not_curl_is_refused()
    {
        Assert.Contains("start with \"curl\"", Fail("wget http://h/"));
    }

    [Fact]
    public void A_curl_command_with_no_url_is_refused()
    {
        Assert.Contains("No URL", Fail("curl -X POST"));
    }

    [Fact]
    public void A_windows_cmd_command_is_recognised_and_the_bash_flavour_suggested()
    {
        Assert.Contains("cURL (bash)", Fail("curl ^\"https://h/x^\" ^\n  -H ^\"A: b^\""));
    }

    [Fact]
    public void The_suggested_name_is_the_method_and_the_path_or_host()
    {
        Assert.Equal("POST /users", Parse("curl -X POST https://api.example.com/users", "https://api.example.com").SuggestedName);
        Assert.Equal("GET other.test/things", Parse("curl https://other.test/things", "https://api.example.com").SuggestedName);
        Assert.Equal("GET /", Parse("curl https://api.example.com", "https://api.example.com").SuggestedName);
    }

    [Fact]
    public void A_realistic_browser_copy_is_imported_whole()
    {
        const string command = """
            curl 'https://api.example.com/v1/orders?expand=items' \
              -H 'accept: application/json, text/plain, */*' \
              -H 'authorization: Bearer eyJhbGciOi.payload.sig' \
              -H 'content-type: application/json' \
              -H 'user-agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64)' \
              --data-raw $'{"sku":"A-1","note":"it\'s \\"fine\\"","qty":2}' \
              --compressed
            """;

        var import = Parse(command, "https://api.example.com/v1");

        Assert.Equal("POST", import.Request.Method);
        Assert.Equal("/orders", import.Request.Path);
        Assert.Equal("expand", Assert.Single(import.Request.QueryParams).Key);
        Assert.Equal(AuthKind.BearerToken, import.Request.Auth);
        Assert.Equal("eyJhbGciOi.payload.sig", import.Request.BearerToken);
        Assert.Equal(BodyKind.Json, import.Request.BodyKind);
        Assert.Contains("\"qty\":2", import.Request.Body);
        Assert.Equal(["accept", "user-agent"], import.Request.Headers.Select(h => h.Key));
        Assert.Empty(import.Warnings);
    }
}
