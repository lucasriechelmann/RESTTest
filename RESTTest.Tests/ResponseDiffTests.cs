using RESTTest.Services;
using RESTTest.ViewModels;

namespace RESTTest.Tests;

/// <summary>The line diff, per-request baselines, and the Diff tab's view model.</summary>
public class ResponseDiffTests
{
    private static string Text(params string[] lines) => string.Join("\n", lines);

    private static string Show(DiffLine line) => line.Kind switch
    {
        DiffKind.Added => "+" + line.Text,
        DiffKind.Removed => "-" + line.Text,
        DiffKind.Gap => "~" + line.Text,
        _ => " " + line.Text,
    };

    // ---- TextDiff ------------------------------------------------------------------------------

    [Fact]
    public void Identical_texts_have_no_differences()
    {
        var diff = TextDiff.Compare(Text("a", "b", "c"), Text("a", "b", "c"));

        Assert.True(diff.IsIdentical);
        Assert.Equal("No differences", diff.Summary);
        Assert.All(diff.Lines, l => Assert.Equal(DiffKind.Same, l.Kind));
        Assert.Equal(3, diff.Lines.Count);
    }

    [Fact]
    public void A_changed_line_is_one_removal_and_one_addition_with_line_numbers()
    {
        var diff = TextDiff.Compare(Text("a", "b", "c"), Text("a", "B", "c"));

        Assert.Equal([" a", "-b", "+B", " c"], diff.Lines.Select(Show));
        Assert.Equal((1, 1), (diff.Added, diff.Removed));
        Assert.Equal("+1 -1", diff.Summary);

        Assert.Equal((1, 1), (diff.Lines[0].OldNumber, diff.Lines[0].NewNumber));
        Assert.Equal((2, (int?)null), (diff.Lines[1].OldNumber, diff.Lines[1].NewNumber));
        Assert.Equal(((int?)null, 2), (diff.Lines[2].OldNumber, diff.Lines[2].NewNumber));
        Assert.Equal((3, 3), (diff.Lines[3].OldNumber, diff.Lines[3].NewNumber));
    }

    [Theory]
    [InlineData("a\nb", "x\na\nb", "+x| a| b")]
    [InlineData("a\nb", "a\nx\nb", " a|+x| b")]
    [InlineData("a\nb", "a\nb\nx", " a| b|+x")]
    [InlineData("x\na\nb", "a\nb", "-x| a| b")]
    [InlineData("a\nx\nb", "a\nb", " a|-x| b")]
    [InlineData("a\nb\nx", "a\nb", " a| b|-x")]
    public void Insertions_and_deletions_at_the_start_middle_and_end(string before, string after, string expected)
    {
        Assert.Equal(expected.Split('|'), TextDiff.Compare(before, after).Lines.Select(Show));
    }

    [Fact]
    public void Empty_sides_are_all_added_or_all_removed()
    {
        var added = TextDiff.Compare("", Text("a", "b"));
        Assert.Equal((2, 0), (added.Added, added.Removed));

        var removed = TextDiff.Compare(Text("a", "b"), null);
        Assert.Equal((0, 2), (removed.Added, removed.Removed));

        Assert.True(TextDiff.Compare("", null).IsIdentical);
    }

    [Fact]
    public void Line_endings_and_a_trailing_newline_do_not_count_as_changes()
    {
        Assert.True(TextDiff.Compare("a\r\nb\r\n", "a\nb").IsIdentical);
        Assert.True(TextDiff.Compare("a\rb", "a\nb\n").IsIdentical);
    }

    [Fact]
    public void A_moved_block_is_reported_as_the_smallest_edit()
    {
        var diff = TextDiff.Compare(Text("a", "b", "c", "d"), Text("c", "d", "a", "b"));

        // Keeping c,d and moving a,b costs 4 edits; keeping a,b costs 4 as well. Never more.
        Assert.Equal(4, diff.Added + diff.Removed);
    }

    [Fact]
    public void A_random_diff_reproduces_both_texts_and_is_as_small_as_possible()
    {
        var random = new Random(12345);

        for (var round = 0; round < 300; round++)
        {
            var a = Enumerable.Range(0, random.Next(0, 14)).Select(_ => ((char)('a' + random.Next(0, 4))).ToString()).ToArray();
            var b = Enumerable.Range(0, random.Next(0, 14)).Select(_ => ((char)('a' + random.Next(0, 4))).ToString()).ToArray();

            var diff = TextDiff.Compare(string.Join("\n", a), string.Join("\n", b));

            Assert.Equal(a, diff.Lines.Where(l => l.Kind != DiffKind.Added).Select(l => l.Text));
            Assert.Equal(b, diff.Lines.Where(l => l.Kind != DiffKind.Removed).Select(l => l.Text));
            Assert.Equal(a.Length + b.Length - 2 * LongestCommonSubsequence(a, b), diff.Added + diff.Removed);
        }
    }

    private static int LongestCommonSubsequence(string[] a, string[] b)
    {
        var table = new int[a.Length + 1, b.Length + 1];
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                table[i, j] = a[i - 1] == b[j - 1] ? table[i - 1, j - 1] + 1 : Math.Max(table[i - 1, j], table[i, j - 1]);
            }
        }

        return table[a.Length, b.Length];
    }

    [Fact]
    public void A_long_document_with_a_few_changes_is_fast_and_exact()
    {
        var before = Enumerable.Range(0, 10_000).Select(i => $"line {i}").ToArray();
        var after = (string[])before.Clone();
        after[10] = "changed A";
        after[5000] = "changed B";
        after[9990] = "changed C";

        var diff = TextDiff.Compare(string.Join("\n", before), string.Join("\n", after));

        Assert.Equal((3, 3), (diff.Added, diff.Removed));
        Assert.Equal(10_003, diff.Lines.Count); // 9,997 unchanged + 3 removed + 3 added
    }

    [Fact]
    public void A_text_that_is_completely_different_is_shown_as_all_removed_then_all_added()
    {
        var before = string.Join("\n", Enumerable.Range(0, 2_500).Select(i => $"old {i}"));
        var after = string.Join("\n", Enumerable.Range(0, 2_500).Select(i => $"new {i}"));

        var diff = TextDiff.Compare(before, after);

        Assert.Equal((2_500, 2_500), (diff.Added, diff.Removed));
        Assert.Equal(5_000, diff.Lines.Count);
    }

    [Fact]
    public void A_text_over_the_line_limit_is_not_compared()
    {
        var huge = string.Join("\n", Enumerable.Repeat("x", TextDiff.MaxLines + 1));

        var diff = TextDiff.Compare(huge, "y");

        Assert.True(diff.TooLarge);
        Assert.False(diff.IsIdentical);
        Assert.Empty(diff.Lines);
    }

    [Fact]
    public void Context_folds_long_unchanged_runs_into_gap_rows()
    {
        var before = Enumerable.Range(1, 20).Select(i => $"l{i}").ToArray();
        var after = (string[])before.Clone();
        after[9] = "CHANGED";

        var lines = TextDiff.Compare(string.Join("\n", before), string.Join("\n", after)).WithContext(3);

        Assert.Equal("~6 unchanged lines", Show(lines[0]));
        Assert.Equal("~7 unchanged lines", Show(lines[^1]));
        Assert.Equal([" l7", " l8", " l9", "-l10", "+CHANGED", " l11", " l12", " l13"], lines.Skip(1).Take(8).Select(Show));
        Assert.Equal(10, lines.Count);
    }

    [Fact]
    public void Context_leaves_an_identical_diff_untouched_and_merges_nearby_changes()
    {
        var same = TextDiff.Compare("a\nb", "a\nb");
        Assert.Equal(same.Lines, same.WithContext(1));

        var near = TextDiff.Compare(Text("a", "b", "c", "d", "e"), Text("A", "b", "c", "d", "E")).WithContext(3);
        Assert.DoesNotContain(near, l => l.Kind == DiffKind.Gap);
    }

    // ---- Baselines -----------------------------------------------------------------------------

    private static HttpExchangeResult Result(string body = "{}", int status = 200, string reason = "OK", bool completed = true) => new()
    {
        Url = "http://h/x",
        Method = "GET",
        Completed = completed,
        StatusCode = status,
        ReasonPhrase = reason,
        Body = body,
        ContentType = "application/json",
    };

    [Fact]
    public void The_first_send_has_no_baseline_and_the_next_compares_with_it()
    {
        var baselines = new ResponseBaselines();
        var request = new RequestViewModel();

        Assert.Null(baselines.ForNextSend(request));

        baselines.Record(request, Result("first"));
        var next = baselines.ForNextSend(request);

        Assert.Equal("first", next!.Result.Body);
        Assert.False(next.Pinned);
        Assert.Null(baselines.ForCurrent(request));

        baselines.Record(request, Result("second"));
        Assert.Equal("first", baselines.ForCurrent(request)!.Result.Body);
        Assert.Equal("second", baselines.ForNextSend(request)!.Result.Body);
    }

    [Fact]
    public void Failed_sends_are_not_recorded()
    {
        var baselines = new ResponseBaselines();
        var request = new RequestViewModel();
        baselines.Record(request, Result("good"));

        baselines.Record(request, Result(completed: false));

        Assert.Equal("good", baselines.ForNextSend(request)!.Result.Body);
    }

    [Fact]
    public void A_pinned_response_stays_the_baseline_until_it_is_unpinned()
    {
        var baselines = new ResponseBaselines();
        var request = new RequestViewModel();

        Assert.False(baselines.PinCurrent(request));

        baselines.Record(request, Result("v1"));
        Assert.True(baselines.PinCurrent(request));

        baselines.Record(request, Result("v2"));
        baselines.Record(request, Result("v3"));

        var pinned = baselines.ForNextSend(request)!;
        Assert.True(pinned.Pinned);
        Assert.Equal("v1", pinned.Result.Body);
        Assert.Equal("v1", baselines.ForCurrent(request)!.Result.Body);

        baselines.Unpin(request);
        Assert.Equal("v3", baselines.ForNextSend(request)!.Result.Body);
        Assert.Equal("v2", baselines.ForCurrent(request)!.Result.Body);
    }

    [Fact]
    public void Each_request_has_its_own_baselines()
    {
        var baselines = new ResponseBaselines();
        var one = new RequestViewModel();
        var two = new RequestViewModel();

        baselines.Record(one, Result("one"));

        Assert.Null(baselines.ForNextSend(two));
        Assert.False(baselines.PinCurrent(two));
    }

    // ---- The Diff tab --------------------------------------------------------------------------

    private static ResponseBaseline Baseline(HttpExchangeResult result, bool pinned = false) =>
        new(result, new DateTime(2026, 11, 6, 12, 31, 2), pinned);

    private static ResponseViewModel Loaded(HttpExchangeResult current, ResponseBaseline? baseline)
    {
        var vm = new ResponseViewModel();
        vm.Load(current, null, baseline);
        return vm;
    }

    [Fact]
    public void Without_a_baseline_the_diff_tab_has_nothing_to_show()
    {
        var vm = Loaded(Result("{\"a\":1}"), null);

        Assert.False(vm.HasBaseline);
        Assert.False(vm.HasDiffLines);
        Assert.Equal(string.Empty, vm.DiffSummary);
        Assert.True(vm.PinBaselineCommand.CanExecute(null));
    }

    [Fact]
    public void A_changed_body_is_summarised_and_listed_with_its_context()
    {
        var before = Result("{\"id\":1,\"name\":\"Ada\",\"tags\":[\"a\"]}");
        var after = Result("{\"id\":1,\"name\":\"Grace\",\"tags\":[\"a\"]}");

        var vm = Loaded(after, Baseline(before));

        Assert.True(vm.HasBaseline);
        Assert.Contains("previous response", vm.DiffNote);
        Assert.Contains("12:31:02", vm.DiffNote);
        Assert.Equal("Body: +1 -1", vm.DiffSummary);
        Assert.Contains(vm.DiffLines, l => l.Kind == DiffKind.Removed && l.Text.Contains("Ada"));
        Assert.Contains(vm.DiffLines, l => l.Kind == DiffKind.Added && l.Text.Contains("Grace"));
        Assert.True(vm.HasDiffLines);
    }

    [Fact]
    public void A_reformatted_but_equal_json_body_is_not_a_difference()
    {
        var compact = Result("{\"a\":1,\"b\":[1,2]}");
        var pretty = Result("{\n    \"a\": 1,\n  \"b\": [ 1, 2 ]\n}");

        var vm = Loaded(pretty, Baseline(compact));

        Assert.Equal("Body: no differences", vm.DiffSummary);
        Assert.False(vm.HasDiffLines);
    }

    [Fact]
    public void A_status_change_is_part_of_the_summary()
    {
        var vm = Loaded(Result("x", 404, "Not Found"), Baseline(Result("x", 200, "OK")));

        Assert.StartsWith("Status 200 OK → 404 Not Found", vm.DiffSummary);
        Assert.EndsWith("Body: no differences", vm.DiffSummary);
    }

    [Fact]
    public void Show_unchanged_lines_lists_everything()
    {
        var lines = Enumerable.Range(1, 30).Select(i => $"line {i}").ToArray();
        var changed = (string[])lines.Clone();
        changed[14] = "different";

        var vm = Loaded(Result(Text(changed)), Baseline(Result(Text(lines))));
        var folded = vm.DiffLines.Count;
        Assert.Contains(vm.DiffLines, l => l.Kind == DiffKind.Gap);

        vm.ShowAllLines = true;

        Assert.True(vm.DiffLines.Count > folded);
        Assert.DoesNotContain(vm.DiffLines, l => l.Kind == DiffKind.Gap);
        Assert.Equal(31, vm.DiffLines.Count);

        vm.ShowAllLines = false;
        Assert.Equal(folded, vm.DiffLines.Count);
    }

    [Fact]
    public void A_pinned_baseline_is_named_as_such()
    {
        var vm = Loaded(Result("b"), Baseline(Result("a"), pinned: true));

        Assert.True(vm.IsPinned);
        Assert.Contains("pinned baseline", vm.DiffNote);
    }

    [Fact]
    public void Binary_bodies_are_compared_byte_for_byte_without_a_line_list()
    {
        HttpExchangeResult Bin(params byte[] bytes) => Result("") with { IsBinary = true, BodyBytes = bytes };

        var same = Loaded(Bin(1, 2, 3), Baseline(Bin(1, 2, 3)));
        Assert.Equal("Binary body: identical", same.DiffSummary);
        Assert.False(same.HasDiffLines);

        var different = Loaded(Bin(1, 2, 3), Baseline(Bin(1, 2)));
        Assert.StartsWith("Binary body: differs", different.DiffSummary);
    }

    [Fact]
    public void A_failed_request_or_a_clear_removes_the_comparison()
    {
        var vm = Loaded(Result("b"), Baseline(Result("a")));
        Assert.True(vm.HasBaseline);

        vm.Load(Result(completed: false), null, Baseline(Result("a")));
        Assert.False(vm.HasBaseline);
        Assert.False(vm.PinBaselineCommand.CanExecute(null));

        vm.Load(Result("b"), null, Baseline(Result("a")));
        vm.Clear();
        Assert.False(vm.HasBaseline);
        Assert.False(vm.HasDiffLines);
        Assert.Equal(string.Empty, vm.DiffNote);
    }

    [Fact]
    public void Pin_and_unpin_call_back_to_whoever_owns_the_memory()
    {
        var vm = Loaded(Result("a"), null);
        var calls = new List<string>();
        vm.PinHandler = () => calls.Add("pin");
        vm.UnpinHandler = () => calls.Add("unpin");

        vm.PinBaselineCommand.Execute(null);
        vm.UnpinBaselineCommand.Execute(null);

        Assert.Equal(["pin", "unpin"], calls);
    }

    [Fact]
    public void Setting_a_baseline_afterwards_recomputes_the_diff()
    {
        var vm = Loaded(Result("b"), null);

        vm.SetBaseline(Baseline(Result("a"), pinned: true));

        Assert.True(vm.IsPinned);
        Assert.Equal("Body: +1 -1", vm.DiffSummary);

        vm.SetBaseline(null);
        Assert.False(vm.HasBaseline);
    }
}
