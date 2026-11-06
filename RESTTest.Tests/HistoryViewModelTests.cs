using RESTTest.Models;
using RESTTest.Services;
using RESTTest.ViewModels;

namespace RESTTest.Tests;

public class HistoryViewModelTests
{
    private static HttpExchangeResult Response(int status = 200, string body = "{\"ok\":true}") => new()
    {
        Url = "http://api.test/things?x=1",
        Method = "GET",
        Completed = true,
        StatusCode = status,
        ReasonPhrase = status == 200 ? "OK" : "Not Found",
        Body = body,
        ContentLength = body.Length,
        ElapsedMilliseconds = 42,
        ResponseHeaders = [new KeyValueModel { Key = "X-Test", Value = "yes" }],
    };

    private static RequestModel Request(string path = "/things") => new() { Method = "GET", Path = path };

    private static HistoryViewModel NewHistory(FakeDialogService? dialogs = null, IEnumerable<HistoryEntryModel>? saved = null) =>
        new(dialogs ?? new FakeDialogService(), saved ?? []);

    private static void Record(HistoryViewModel history, string path = "/things", DateTimeOffset? at = null, HttpExchangeResult? result = null) =>
        history.Record("Sample", Request(path), result ?? Response(), [], at);

    [Fact]
    public void A_recorded_send_goes_to_the_top_with_the_facts_of_the_exchange()
    {
        var history = NewHistory();
        var tests = new List<AssertionResult>
        {
            new(true, "a", "a"),
            new(false, "b", "b"),
            new(true, "c", "c"),
        };

        history.Record("Sample", Request("/older"), Response(), []);
        history.Record("Sample", Request("/newer"), Response(404, "{}"), tests);

        var top = history.Entries[0];
        Assert.Equal("/newer", top.Path);
        Assert.Equal("Sample", top.CollectionName);
        Assert.Equal("http://api.test/things?x=1", top.Url);
        Assert.Equal("404 Not Found", top.StatusText);
        Assert.Equal("ClientError", top.StatusCategory);
        Assert.Equal(2, top.Model.TestsPassed);
        Assert.Equal(3, top.Model.TestsTotal);
        Assert.False(top.AllTestsPassed);
        Assert.Equal("2/3 tests passed", top.TestSummary);
        Assert.Equal("/older", history.Entries[1].Path);
    }

    [Fact]
    public void An_entry_without_assertions_reports_no_tests()
    {
        var history = NewHistory();
        Record(history);

        Assert.False(history.Entries[0].HasTests);
        Assert.Equal(string.Empty, history.Entries[0].TestSummary);
    }

    [Fact]
    public void A_transport_failure_is_recorded_as_an_error_entry()
    {
        var history = NewHistory();
        var failure = HttpExchangeResult.Failure("http://api.test/x", "GET", "Connection refused", 7);

        Record(history, result: failure);

        var entry = history.Entries[0];
        Assert.True(entry.HasError);
        Assert.Equal("ERR", entry.StatusLabel);
        Assert.Equal("No response", entry.StatusText);
        Assert.Equal("Connection refused", entry.Error);
        Assert.Equal(0, entry.StatusCode);
        Assert.Equal(string.Empty, entry.SizeText);
    }

    [Fact]
    public void History_is_capped_and_the_oldest_entries_are_dropped()
    {
        var history = NewHistory();

        for (var i = 0; i < HistoryViewModel.MaxEntries + 5; i++)
        {
            Record(history, path: $"/r{i}");
        }

        Assert.Equal(HistoryViewModel.MaxEntries, history.Entries.Count);
        Assert.Equal($"/r{HistoryViewModel.MaxEntries + 4}", history.Entries[0].Path);
        Assert.DoesNotContain(history.Entries, e => e.Path == "/r0");
    }

    [Fact]
    public void A_huge_response_body_is_truncated_and_flagged()
    {
        var history = NewHistory();
        var body = new string('x', HistoryViewModel.MaxBodyChars + 500);

        Record(history, result: Response(body: body));

        var entry = history.Entries[0];
        Assert.True(entry.BodyTruncated);
        Assert.Equal(HistoryViewModel.MaxBodyChars, entry.RawBody.Length);
        Assert.Equal(body.Length, entry.Model.ContentLength);
    }

    [Fact]
    public void A_body_at_the_limit_is_kept_whole()
    {
        var history = NewHistory();

        Record(history, result: Response(body: new string('x', HistoryViewModel.MaxBodyChars)));

        Assert.False(history.Entries[0].BodyTruncated);
    }

    [Fact]
    public void Recording_raises_Changed_so_the_owner_can_queue_a_save()
    {
        var history = NewHistory();
        var raised = 0;
        history.Changed += (_, _) => raised++;

        Record(history);

        Assert.Equal(1, raised);
    }

    [Fact]
    public void Loading_saved_entries_shows_the_newest_first_within_the_cap()
    {
        var now = DateTimeOffset.Now;
        var saved = Enumerable.Range(0, HistoryViewModel.MaxEntries + 10)
            .Select(i => new HistoryEntryModel { Timestamp = now.AddMinutes(i), Request = Request($"/r{i}") })
            .OrderBy(_ => Guid.NewGuid());

        var history = NewHistory(saved: saved);

        Assert.Equal(HistoryViewModel.MaxEntries, history.Entries.Count);
        Assert.Equal($"/r{HistoryViewModel.MaxEntries + 9}", history.Entries[0].Path);
    }

    [Fact]
    public void Removing_the_selected_entry_selects_its_neighbour_and_raises_Changed()
    {
        var history = NewHistory();
        Record(history, "/a");
        Record(history, "/b");
        Record(history, "/c");
        history.Selected = history.Entries[1];
        var raised = 0;
        history.Changed += (_, _) => raised++;

        history.RemoveSelectedCommand.Execute(null);

        Assert.Equal(["/c", "/a"], history.Entries.Select(e => e.Path));
        Assert.Equal("/a", history.Selected?.Path);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Removing_the_last_entry_clears_the_selection()
    {
        var history = NewHistory();
        Record(history);
        history.Selected = history.Entries[0];

        history.RemoveSelectedCommand.Execute(null);

        Assert.Empty(history.Entries);
        Assert.Null(history.Selected);
        Assert.False(history.HasEntries);
    }

    [Fact]
    public void Delete_and_save_are_unavailable_until_something_is_selected()
    {
        var history = NewHistory();
        Record(history);

        Assert.False(history.RemoveSelectedCommand.CanExecute(null));
        Assert.False(history.SaveAsRequestCommand.CanExecute(null));

        history.Selected = history.Entries[0];

        Assert.True(history.RemoveSelectedCommand.CanExecute(null));
        Assert.True(history.SaveAsRequestCommand.CanExecute(null));
    }

    [Fact]
    public void Clearing_asks_first_and_does_nothing_when_declined()
    {
        var dialogs = new FakeDialogService { ConfirmResult = false };
        var history = NewHistory(dialogs);
        Record(history);
        Record(history);

        history.ClearAllCommand.Execute(null);

        Assert.Equal(2, history.Entries.Count);
        Assert.Contains("all 2 entries", Assert.Single(dialogs.ConfirmMessages));
    }

    [Fact]
    public void The_clear_prompt_reads_correctly_for_a_single_entry()
    {
        var dialogs = new FakeDialogService { ConfirmResult = false };
        var history = NewHistory(dialogs);
        Record(history);

        history.ClearAllCommand.Execute(null);

        Assert.Contains("the only entry", Assert.Single(dialogs.ConfirmMessages));
    }

    [Fact]
    public void Clearing_when_confirmed_empties_the_list_and_raises_Changed()
    {
        var history = NewHistory();
        Record(history);
        history.Selected = history.Entries[0];
        var raised = 0;
        history.Changed += (_, _) => raised++;

        history.ClearAllCommand.Execute(null);

        Assert.Empty(history.Entries);
        Assert.Null(history.Selected);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Clearing_an_empty_history_does_not_ask()
    {
        var dialogs = new FakeDialogService();
        var history = NewHistory(dialogs);

        history.ClearAllCommand.Execute(null);

        Assert.Empty(dialogs.ConfirmMessages);
    }

    [Fact]
    public void Save_as_request_hands_the_selected_entry_to_the_owner_and_shows_its_message()
    {
        var history = NewHistory();
        Record(history, "/a");
        HistoryEntryViewModel? received = null;
        history.SaveAsRequestHandler = entry =>
        {
            received = entry;
            return "Saved it.";
        };
        history.Selected = history.Entries[0];

        history.SaveAsRequestCommand.Execute(null);

        Assert.Same(history.Entries[0], received);
        Assert.Equal("Saved it.", history.StatusMessage);
    }

    [Fact]
    public void Selecting_another_entry_clears_the_previous_message()
    {
        var history = NewHistory();
        Record(history, "/a");
        Record(history, "/b");
        history.SaveAsRequestHandler = _ => "Saved it.";
        history.Selected = history.Entries[0];
        history.SaveAsRequestCommand.Execute(null);

        history.Selected = history.Entries[1];

        Assert.Null(history.StatusMessage);
    }

    [Fact]
    public void The_pretty_body_is_formatted_json_and_other_text_is_left_alone()
    {
        var history = NewHistory();
        Record(history, result: Response(body: "{\"a\":1}"));
        Record(history, result: Response(body: "<html>nope</html>"));

        Assert.Contains("\n", history.Entries[1].PrettyBody);
        Assert.Equal("<html>nope</html>", history.Entries[0].PrettyBody);
    }

    [Fact]
    public void The_request_summary_counts_only_enabled_rows_and_mentions_auth()
    {
        var history = NewHistory();
        var request = new RequestModel
        {
            Auth = AuthKind.BearerToken,
            Headers =
            [
                new KeyValueModel { Key = "A", Enabled = true },
                new KeyValueModel { Key = "B", Enabled = false },
            ],
            QueryParams =
            [
                new KeyValueModel { Key = "q", Enabled = true },
                new KeyValueModel { Key = "r", Enabled = true },
            ],
        };

        history.Record("Sample", request, Response(), []);

        Assert.Equal("1 header · 2 query params · Bearer auth", history.Entries[0].RequestSummary);
        Assert.Equal("No headers, parameters or auth", NewHistoryWith(Request()).Entries[0].RequestSummary);
    }

    [Fact]
    public void ToModels_returns_the_entries_in_display_order()
    {
        var history = NewHistory();
        Record(history, "/a");
        Record(history, "/b");

        Assert.Equal(["/b", "/a"], history.ToModels().Select(m => m.Request.Path));
    }

    private static HistoryViewModel NewHistoryWith(RequestModel request)
    {
        var history = NewHistory();
        history.Record("Sample", request, Response(), []);
        return history;
    }
}
