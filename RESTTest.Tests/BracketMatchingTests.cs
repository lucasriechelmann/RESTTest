using RESTTest.Services;

namespace RESTTest.Tests;

/// <summary>
/// Covers the bracket pair highlighted around the caret. The interesting cases are brackets
/// inside JSON string literals, which a depth-counting scan would wrongly treat as structural.
/// </summary>
public class BracketMatchingTests
{
    private static BracketMatch Match(string text, int caret)
    {
        Assert.True(JsonBracketMatcher.TryFindMatch(text, caret, out var match),
            $"expected a match at offset {caret} of \"{text}\"");

        return match;
    }

    private static void AssertNoMatch(string? text, int caret) =>
        Assert.False(JsonBracketMatcher.TryFindMatch(text, caret, out _));

    [Fact]
    public void Matches_the_closer_when_the_caret_follows_an_opener()
    {
        // "{|}" - caret just after the brace, which is where typing leaves it.
        Assert.Equal(new BracketMatch(0, 1), Match("{}", 1));
    }

    [Fact]
    public void Matches_when_the_caret_sits_on_the_opener()
    {
        Assert.Equal(new BracketMatch(0, 1), Match("{}", 0));
    }

    [Fact]
    public void Matches_the_opener_when_the_caret_follows_a_closer()
    {
        Assert.Equal(new BracketMatch(0, 1), Match("{}", 2));
    }

    [Fact]
    public void Resolves_the_correct_partner_when_nested()
    {
        const string json = "{\"a\":{\"b\":1}}";
        //                   0 1234 5 6789...

        Assert.Equal(new BracketMatch(0, 12), Match(json, 1));
        Assert.Equal(new BracketMatch(5, 11), Match(json, 6));
    }

    [Fact]
    public void Handles_arrays_and_picks_the_innermost_pair_at_the_caret()
    {
        const string json = "[1,[2],3]";

        Assert.Equal(new BracketMatch(0, 8), Match(json, 1));
        Assert.Equal(new BracketMatch(3, 5), Match(json, 4));
    }

    [Fact]
    public void Brackets_inside_a_string_are_not_structural()
    {
        // The closing brace the matcher must pick is the real one at the end, not the quoted one.
        const string json = "{\"a\": \"}\"}";

        Assert.Equal(new BracketMatch(0, 9), Match(json, 1));
    }

    [Fact]
    public void A_bracket_inside_a_string_has_no_partner()
    {
        const string json = "{\"a\": \"}\"}";

        // Offset 7 is the quoted brace; the caret after it must highlight nothing.
        AssertNoMatch(json, 8);
    }

    [Fact]
    public void An_escaped_quote_does_not_end_the_string()
    {
        // {"a":"\"}"}  - the brace sits inside a literal that contains an escaped quote.
        const string json = """{"a":"\"}"}""";

        Assert.Equal(new BracketMatch(0, 10), Match(json, 1));
    }

    [Fact]
    public void A_bracket_in_a_string_does_not_unbalance_later_pairs()
    {
        const string json = "{\"a\": \"[[[\", \"b\": [1]}";

        var outer = Match(json, 1);
        Assert.Equal(0, outer.OpenOffset);
        Assert.Equal(json.Length - 1, outer.CloseOffset);
    }

    [Fact]
    public void Unmatched_openers_report_no_match() => AssertNoMatch("{\"a\": 1", 1);

    [Fact]
    public void Unmatched_closers_report_no_match() => AssertNoMatch("a}", 2);

    [Fact]
    public void Mismatched_pairs_report_no_match() => AssertNoMatch("{]", 1);

    [Fact]
    public void A_caret_away_from_any_bracket_reports_no_match() => AssertNoMatch("{\"abc\": 1}", 4);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_documents_report_no_match(string? text) => AssertNoMatch(text, 0);

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void Offsets_outside_the_document_report_no_match(int caret) => AssertNoMatch("{}", caret);

    [Fact]
    public void Very_large_documents_are_skipped_to_keep_caret_movement_cheap()
    {
        // Matching runs on every caret move, so there is a deliberate size ceiling.
        var huge = "{" + new string(' ', JsonBracketMatcher.MaxDocumentLength) + "}";

        AssertNoMatch(huge, 1);
    }

    [Fact]
    public void A_realistic_formatted_body_matches_across_lines()
    {
        var json = string.Join(Environment.NewLine,
            "{",
            "  \"title\": \"Hello\",",
            "  \"tags\": [\"a\", \"b\"]",
            "}");

        var match = Match(json, 1);

        Assert.Equal(0, match.OpenOffset);
        Assert.Equal(json.Length - 1, match.CloseOffset);
    }
}
