using System.Text.Json;
using RESTTest.Services;
using RESTTest.ViewModels;

namespace RESTTest.Tests;

/// <summary>The multi-match JSONPath engine behind the response filter, and the filter on the response view model.</summary>
public class JsonPathQueryTests
{
    private const string Store = """
        {
          "store": {
            "book": [
              { "category": "reference", "author": "Nigel Rees", "title": "Sayings of the Century", "price": 8.95 },
              { "category": "fiction", "author": "Evelyn Waugh", "title": "Sword of Honour", "price": 12.99 },
              { "category": "fiction", "author": "Herman Melville", "title": "Moby Dick", "isbn": "0-553-21311-3", "price": 8.99 },
              { "category": "fiction", "author": "J. R. R. Tolkien", "title": "The Lord of the Rings", "isbn": "0-395-19395-8", "price": 22.99 }
            ],
            "bicycle": { "color": "red", "price": 19.95 }
          },
          "expensive": 10,
          "a.b": 1,
          "tags": ["x", "y", "z", "w"]
        }
        """;

    private static string[] Values(string path, string json = Store)
    {
        var result = JsonPathQuery.Run(json, path);
        Assert.True(result.Ok, result.Error);
        return [.. result.Matches.Select(m => m.Json)];
    }

    private static string[] Strings(string path, string json = Store) =>
        [.. Values(path, json).Select(v => JsonSerializer.Deserialize<string>(v)!)];

    // ---- Selecting -----------------------------------------------------------------------------

    [Theory]
    [InlineData("$.store.bicycle.color")]
    [InlineData("store.bicycle.color")]
    [InlineData("$['store']['bicycle']['color']")]
    [InlineData("$.store['bicycle'].color")]
    [InlineData("  $.store.bicycle.color  ")]
    public void A_plain_path_selects_one_value_in_any_spelling(string path)
    {
        Assert.Equal(["\"red\""], Values(path));
    }

    [Fact]
    public void A_bare_dollar_selects_the_whole_document()
    {
        var result = JsonPathQuery.Run(Store, "$");

        Assert.Single(result.Matches);
        Assert.Equal("$", result.Matches[0].Path);
        Assert.True(result.IsDefinite);
    }

    [Fact]
    public void Index_wildcard_union_and_slices_select_array_items()
    {
        Assert.Equal(["Sayings of the Century"], Strings("$.store.book[0].title"));
        Assert.Equal(["The Lord of the Rings"], Strings("$.store.book[-1].title"));
        Assert.Equal(["Nigel Rees", "Evelyn Waugh", "Herman Melville", "J. R. R. Tolkien"], Strings("$.store.book[*].author"));
        Assert.Equal(["Sayings of the Century", "Moby Dick"], Strings("$.store.book[0,2].title"));
        Assert.Equal(["Sword of Honour", "Moby Dick"], Strings("$.store.book[1:3].title"));
        Assert.Equal(["Sayings of the Century", "Moby Dick"], Strings("$.store.book[::2].title"));
        Assert.Equal(["Moby Dick", "The Lord of the Rings"], Strings("$.store.book[-2:].title"));
        Assert.Equal(["The Lord of the Rings", "Moby Dick", "Sword of Honour", "Sayings of the Century"], Strings("$.store.book[::-1].title"));
        Assert.Equal(["x", "w"], Strings("$.tags[0,-1]"));
    }

    [Fact]
    public void A_slice_with_step_zero_or_out_of_range_selects_nothing_rather_than_failing()
    {
        Assert.Empty(Values("$.tags[::0]"));
        Assert.Empty(Values("$.tags[10:20]"));
        Assert.Equal(["x", "y", "z", "w"], Strings("$.tags[-100:100]"));
    }

    [Fact]
    public void Wildcard_on_an_object_selects_its_values_in_order()
    {
        var result = JsonPathQuery.Run(Store, "$.store.*");

        Assert.Equal(["$['store']['book']", "$['store']['bicycle']"], result.Matches.Select(m => m.Path));
    }

    [Fact]
    public void Recursive_descent_finds_names_at_any_depth_in_document_order()
    {
        Assert.Equal(["Nigel Rees", "Evelyn Waugh", "Herman Melville", "J. R. R. Tolkien"], Strings("$..author"));
        Assert.Equal(["8.95", "12.99", "8.99", "22.99", "19.95"], Values("$..price"));
        Assert.Equal(["Moby Dick"], Strings("$..book[2].title"));
        Assert.Equal(4, Values("$.store..category").Length);
    }

    [Fact]
    public void Matches_carry_their_normalised_path()
    {
        var result = JsonPathQuery.Run(Store, "$..book[1].title");

        Assert.Equal("$['store']['book'][1]['title']", Assert.Single(result.Matches).Path);
    }

    [Fact]
    public void A_name_with_a_dot_a_quote_or_a_backslash_is_reachable_and_escaped_in_the_path()
    {
        Assert.Equal(["1"], Values("$['a.b']"));

        var odd = JsonPathQuery.Run("""{ "it's": { "back\\slash": 5 } }""", "$..*");
        Assert.Equal("$['it\\'s']", odd.Matches[0].Path);
        Assert.Equal("$['it\\'s']['back\\\\slash']", odd.Matches[1].Path);
    }

    [Fact]
    public void A_missing_path_is_a_successful_query_with_no_matches()
    {
        var result = JsonPathQuery.Run(Store, "$.store.nothing");

        Assert.True(result.Ok);
        Assert.Empty(result.Matches);
        Assert.Equal("No matches", result.Summary);
        Assert.Equal(string.Empty, result.ToDisplayJson());
        Assert.Empty(Values("$.store.book[10]"));
        Assert.Empty(Values("$.expensive.deeper"));
        Assert.Empty(Values("$.tags.name"));
    }

    // ---- Filters -------------------------------------------------------------------------------

    [Fact]
    public void Filters_compare_numbers_and_strings()
    {
        Assert.Equal(["Sayings of the Century", "Moby Dick"], Strings("$..book[?(@.price < 10)].title"));
        Assert.Equal(["The Lord of the Rings"], Strings("$..book[?(@.price >= 22.99)].title"));
        Assert.Equal(["Sword of Honour", "The Lord of the Rings"], Strings("$..book[?(@.category == 'fiction' && @.price > 10)].title"));
        Assert.Equal(["Sayings of the Century"], Strings("$..book[?(@.category != 'fiction')].title"));
        Assert.Equal(["Sayings of the Century", "Moby Dick", "The Lord of the Rings"], Strings("$..book[?(@.price < 9 || @.price > 20)].title"));
    }

    [Fact]
    public void Filters_test_existence_and_negation_and_group_with_parentheses()
    {
        Assert.Equal(["Moby Dick", "The Lord of the Rings"], Strings("$..book[?(@.isbn)].title"));
        Assert.Equal(["Sayings of the Century", "Sword of Honour"], Strings("$..book[?(!@.isbn)].title"));
        Assert.Equal(["Moby Dick"], Strings("$..book[?((@.price < 10 || @.price > 20) && @.isbn && @.price < 9)].title"));
    }

    [Fact]
    public void A_filter_without_parentheses_works_too()
    {
        Assert.Equal(["The Lord of the Rings"], Strings("$..book[?@.price>20].title"));
    }

    [Fact]
    public void Filters_can_use_regular_expressions_length_and_the_root()
    {
        Assert.Equal(["Sword of Honour", "Moby Dick"], Strings("$..book[?(@.author =~ 'Herman|Evelyn')].title"));
        Assert.Equal(["Sayings of the Century", "The Lord of the Rings"], Strings("$..book[?(length(@.title) > 20)].title"));
        Assert.Equal(["Sayings of the Century", "Moby Dick"], Strings("$..book[?(@.price < $.expensive)].title"));
        Assert.Equal(["Nigel Rees"], Strings("$..book[?(@.author =~ '^N')].author"));
    }

    [Fact]
    public void A_bare_at_sign_stands_for_the_item_itself()
    {
        Assert.Equal(["y"], Strings("$.tags[?(@ == 'y')]"));
        Assert.Equal(["y", "z"], Strings("$.tags[?(@ > 'x')]"));
        Assert.Equal(["\"red\""], Values("$.store.bicycle[?(@ == 'red')]"));
    }

    [Fact]
    public void Filters_follow_equality_rules_for_missing_booleans_null_and_mixed_types()
    {
        const string Mixed = """[ { "a": true }, { "a": false }, { "a": null }, { "b": 1 }, { "a": "8.95" }, { "a": 8.95 } ]""";

        Assert.Single(Values("$[?(@.a == true)]", Mixed));
        Assert.Single(Values("$[?(@.a == false)]", Mixed));
        Assert.Single(Values("$[?(@.a == null)]", Mixed));
        Assert.Equal(5, Values("$[?(@.a != null)]", Mixed).Length);
        Assert.Single(Values("$[?(@.a == 8.95)]", Mixed)); // the string "8.95" is not the number 8.95
        Assert.Single(Values("$[?(@.a == '8.95')]", Mixed));

        // Missing equals missing: the items that have no "isbn" match "@.isbn == @.nothing".
        Assert.Equal(2, Values("$..book[?(@.isbn == @.nothing)]").Length);
        Assert.Empty(Values("$[?(@.zzz < 5)]", Mixed));
    }

    // ---- Display -------------------------------------------------------------------------------

    [Fact]
    public void A_definite_path_shows_its_value_and_a_wildcard_shows_an_array()
    {
        var one = JsonPathQuery.Run(Store, "$.store.book[0].title");
        Assert.True(one.IsDefinite);
        Assert.Equal("\"Sayings of the Century\"", one.ToDisplayJson());
        Assert.Equal("1 match", one.Summary);

        var many = JsonPathQuery.Run(Store, "$.tags[*]");
        Assert.False(many.IsDefinite);
        Assert.Equal("4 matches", many.Summary);
        using var parsed = JsonDocument.Parse(many.ToDisplayJson());
        Assert.Equal(4, parsed.RootElement.GetArrayLength());

        // A wildcard that happens to match once is still an array, so the shape never depends on the data.
        var single = JsonPathQuery.Run("""{ "a": [1] }""", "$.a[*]");
        Assert.StartsWith("[", single.ToDisplayJson());
    }

    [Fact]
    public void More_matches_than_the_cap_are_cut_and_the_summary_says_so()
    {
        var big = "[" + string.Join(",", Enumerable.Range(0, JsonPathQuery.MaxMatches + 50)) + "]";

        var result = JsonPathQuery.Run(big, "$[*]");

        Assert.True(result.Truncated);
        Assert.Equal(JsonPathQuery.MaxMatches, result.Matches.Count);
        Assert.Contains("more not shown", result.Summary);
    }

    // ---- Mistakes ------------------------------------------------------------------------------

    [Theory]
    [InlineData("", "Type a path")]
    [InlineData("   ", "Type a path")]
    [InlineData("$.store.book[", "never closed")]
    [InlineData("$.store.book[abc]", "Unexpected 'a'")]
    [InlineData("$.store.book[0", "never closed")]
    [InlineData("$.store..", "name is missing")]
    [InlineData("$.store.", "name is missing")]
    [InlineData("$ x", "Unexpected 'x'")]
    [InlineData("$.store.book[?(@.price <)]", "")]
    [InlineData("$.store.book[?(@.price > 10]", "never closed")]
    [InlineData("$.store.book[?(@.title =~ 'a(')]", "regular expression")]
    [InlineData("$.store.book[?(@.title =~ 5)]", "quoted regular expression")]
    [InlineData("$.store.book[?(5)]", "needs a comparison")]
    [InlineData("$.store.book[?(@.title == oops)]", "not a value")]
    [InlineData("$.store.book['name]", "never closed")]
    [InlineData("$.store.book[1:2:3:4]", "slice")]
    public void Mistakes_are_explained_instead_of_thrown(string path, string expectedFragment)
    {
        var result = JsonPathQuery.Run(Store, path);

        Assert.False(result.Ok);
        Assert.Empty(result.Matches);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.Contains(expectedFragment, result.Error);
        Assert.Equal(result.Error, result.Summary);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html>nope</html>")]
    [InlineData("{ broken")]
    public void A_body_that_is_not_json_is_reported(string body)
    {
        var result = JsonPathQuery.Run(body, "$.a");

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void A_scalar_or_array_root_works()
    {
        Assert.Equal(["2"], Values("$[1]", "[1,2,3]"));
        Assert.Equal(["42"], Values("$", "42"));
        Assert.Equal(["\"hi\""], Values("$", "\"hi\""));
    }

    // ---- The response filter -------------------------------------------------------------------

    private static HttpExchangeResult Response(string body, bool binary = false) => new()
    {
        Url = "http://h/x",
        Method = "GET",
        Completed = true,
        StatusCode = 200,
        ReasonPhrase = "OK",
        Body = body,
        IsBinary = binary,
        ContentType = "application/json",
    };

    [Fact]
    public void Without_a_filter_the_preview_is_the_whole_pretty_body()
    {
        var vm = new ResponseViewModel();
        vm.Load(Response("{\"a\":1}"));

        Assert.Equal(vm.PrettyBody, vm.DisplayBody);
        Assert.Equal(string.Empty, vm.FilterStatus);
        Assert.True(vm.CanFilter);
    }

    [Fact]
    public void A_filter_narrows_the_preview_and_counts_the_matches()
    {
        var vm = new ResponseViewModel();
        vm.Load(Response(Store));

        vm.FilterText = "$..book[?(@.price < 10)].title";

        Assert.Equal("2 matches", vm.FilterStatus);
        Assert.False(vm.FilterHasError);
        Assert.Contains("Sayings of the Century", vm.DisplayBody);
        Assert.Contains("Moby Dick", vm.DisplayBody);
        Assert.DoesNotContain("Sword of Honour", vm.DisplayBody);
        Assert.NotEqual(vm.PrettyBody, vm.DisplayBody);
    }

    [Fact]
    public void A_path_with_no_match_shows_nothing_and_says_so()
    {
        var vm = new ResponseViewModel();
        vm.Load(Response(Store));

        vm.FilterText = "$.nothing";

        Assert.Equal("No matches", vm.FilterStatus);
        Assert.Equal(string.Empty, vm.DisplayBody);
        Assert.False(vm.FilterHasError);
    }

    [Fact]
    public void A_half_typed_path_keeps_the_whole_body_in_view_and_shows_the_problem()
    {
        var vm = new ResponseViewModel();
        vm.Load(Response(Store));

        vm.FilterText = "$.store.book[";

        Assert.True(vm.FilterHasError);
        Assert.Contains("never closed", vm.FilterStatus);
        Assert.Equal(vm.PrettyBody, vm.DisplayBody);

        vm.FilterText = "$.store.book[0].title";
        Assert.False(vm.FilterHasError);
        Assert.Contains("Sayings of the Century", vm.DisplayBody);
    }

    [Fact]
    public void Clearing_the_filter_restores_the_whole_body()
    {
        var vm = new ResponseViewModel();
        vm.Load(Response(Store));
        vm.FilterText = "$.tags";

        vm.FilterText = "";

        Assert.Equal(vm.PrettyBody, vm.DisplayBody);
        Assert.Equal(string.Empty, vm.FilterStatus);
    }

    [Fact]
    public void The_filter_survives_a_resend_and_is_applied_to_the_new_body()
    {
        var vm = new ResponseViewModel();
        vm.Load(Response("{\"count\":1}"));
        vm.FilterText = "$.count";

        vm.Load(Response("{\"count\":2}"));

        Assert.Equal("$.count", vm.FilterText);
        Assert.Equal("2", vm.DisplayBody.Trim());
    }

    [Fact]
    public void Selecting_another_request_clears_the_filter()
    {
        var vm = new ResponseViewModel();
        vm.Load(Response(Store));
        vm.FilterText = "$.tags";

        vm.Clear();

        Assert.Equal(string.Empty, vm.FilterText);
        Assert.Equal(string.Empty, vm.DisplayBody);
        Assert.False(vm.CanFilter);
    }

    [Fact]
    public void A_filter_on_a_body_that_is_not_json_reports_it()
    {
        var vm = new ResponseViewModel();
        vm.Load(Response("plain text, not json"));

        vm.FilterText = "$.a";

        Assert.True(vm.FilterHasError);
        Assert.Contains("not JSON", vm.FilterStatus);
        Assert.Equal(vm.PrettyBody, vm.DisplayBody);
    }

    [Fact]
    public void Binary_and_failed_responses_have_no_filter()
    {
        var vm = new ResponseViewModel();

        vm.Load(Response("", binary: true));
        Assert.False(vm.CanFilter);

        vm.Load(HttpExchangeResult.Failure("u", "GET", "refused", 1));
        Assert.False(vm.CanFilter);

        vm.Load(Response("{}"));
        Assert.True(vm.CanFilter);
    }
}
