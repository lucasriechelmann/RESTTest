using RESTTest.Models;
using RESTTest.Services;
using RESTTest.ViewModels;

namespace RESTTest.Tests;

public class RunnerViewModelTests
{
    private sealed class FakeSender : IHttpRequestSender
    {
        public Func<RequestModel, CancellationToken, Task<HttpExchangeResult>> Respond { get; set; } =
            (request, _) => Task.FromResult(Ok(200));

        public List<(string? BaseUrl, string Path)> Calls { get; } = [];

        public string BuildUrl(string? baseUrl, RequestModel request) => (baseUrl ?? string.Empty) + request.Path;

        public Task<HttpExchangeResult> SendAsync(string? baseUrl, RequestModel request, CancellationToken cancellationToken)
        {
            Calls.Add((baseUrl, request.Path));
            return Respond(request, cancellationToken);
        }
    }

    private static HttpExchangeResult Ok(int status, string body = "") => new()
    {
        Url = "http://test",
        Method = "GET",
        Completed = true,
        StatusCode = status,
        ReasonPhrase = status == 200 ? "OK" : "Other",
        Body = body,
        ElapsedMilliseconds = 5,
    };

    private static RunItemViewModel Item(string name, string path, params AssertionModel[] assertions) =>
        new(name, new RequestModel { Path = path, Assertions = [.. assertions] });

    private static AssertionModel StatusIs(string expected) => new()
    {
        Target = AssertionTarget.StatusCode,
        Operator = AssertionOperator.EqualTo,
        Expected = expected,
    };

    private static RunnerViewModel Runner(FakeSender sender, params RunItemViewModel[] items) =>
        new(sender, "http://api.test", "Test run", items);

    [Fact]
    public async Task Sends_each_request_in_order_against_the_one_base_url()
    {
        var sender = new FakeSender();
        var runner = Runner(sender, Item("a", "/a"), Item("b", "/b"), Item("c", "/c"));

        await runner.RunAsync();

        Assert.Equal(
            [("http://api.test", "/a"), ("http://api.test", "/b"), ("http://api.test", "/c")],
            sender.Calls);
    }

    [Fact]
    public async Task A_request_whose_assertions_hold_passes()
    {
        var runner = Runner(new FakeSender(), Item("a", "/a", StatusIs("200")));

        await runner.RunAsync();

        Assert.Equal(RunItemState.Passed, runner.Items[0].State);
        Assert.True(runner.AllPassed);
        Assert.Equal(1, runner.Items[0].AssertionsPassed);
    }

    [Fact]
    public async Task A_failed_assertion_fails_the_request_and_records_why()
    {
        var runner = Runner(new FakeSender(), Item("a", "/a", StatusIs("201")));

        await runner.RunAsync();

        var item = runner.Items[0];
        Assert.Equal(RunItemState.Failed, item.State);
        Assert.Single(item.Failures);
        Assert.Contains("201", item.Failures[0]);
        Assert.False(runner.AllPassed);
        Assert.True(runner.HasFailures);
    }

    [Fact]
    public async Task One_failure_does_not_stop_the_rest_of_the_run()
    {
        var runner = Runner(
            new FakeSender(),
            Item("a", "/a", StatusIs("500")),
            Item("b", "/b", StatusIs("200")));

        await runner.RunAsync();

        Assert.Equal(RunItemState.Failed, runner.Items[0].State);
        Assert.Equal(RunItemState.Passed, runner.Items[1].State);
        Assert.Equal(2, runner.CompletedCount);
    }

    [Fact]
    public async Task A_request_without_assertions_is_reported_as_unchecked_not_passed()
    {
        var runner = Runner(new FakeSender(), Item("a", "/a"));

        await runner.RunAsync();

        Assert.Equal(RunItemState.Unchecked, runner.Items[0].State);
        Assert.Contains("without tests", runner.Summary);
        Assert.True(runner.AllPassed);
    }

    [Fact]
    public async Task A_transport_failure_fails_the_request_even_without_assertions()
    {
        var sender = new FakeSender
        {
            Respond = (_, _) => Task.FromResult(HttpExchangeResult.Failure("http://test", "GET", "Connection refused", 1)),
        };
        var runner = Runner(sender, Item("a", "/a"));

        await runner.RunAsync();

        Assert.Equal(RunItemState.Failed, runner.Items[0].State);
        Assert.Equal("No response", runner.Items[0].StatusText);
        Assert.Equal(["Connection refused"], runner.Items[0].Failures);
    }

    [Fact]
    public async Task The_summary_counts_requests_and_assertions()
    {
        var runner = Runner(
            new FakeSender(),
            Item("a", "/a", StatusIs("200"), StatusIs("200")),
            Item("b", "/b", StatusIs("404")));

        await runner.RunAsync();

        Assert.Equal("1 passed · 1 failed · 2/3 assertions", runner.Summary);
    }

    [Fact]
    public async Task Cancelling_skips_the_request_in_flight_and_everything_after_it()
    {
        var started = new TaskCompletionSource();
        var sender = new FakeSender
        {
            Respond = async (request, token) =>
            {
                if (request.Path == "/b")
                {
                    started.SetResult();
                    try
                    {
                        await Task.Delay(Timeout.Infinite, token);
                    }
                    catch (OperationCanceledException)
                    {
                        return HttpExchangeResult.Failure("http://test", "GET", "Cancelled", 1);
                    }
                }

                return Ok(200);
            },
        };
        var runner = Runner(sender, Item("a", "/a", StatusIs("200")), Item("b", "/b"), Item("c", "/c"));

        var run = runner.RunAsync();
        await started.Task;
        runner.CancelCommand.Execute(null);
        await run;

        Assert.Equal(RunItemState.Passed, runner.Items[0].State);
        Assert.Equal(RunItemState.Skipped, runner.Items[1].State);
        Assert.Equal(RunItemState.Skipped, runner.Items[2].State);
        Assert.True(runner.WasCancelled);
        Assert.False(runner.AllPassed);
        Assert.False(runner.IsRunning);
        Assert.Single(sender.Calls, c => c.Path == "/a");
        Assert.DoesNotContain(sender.Calls, c => c.Path == "/c");
    }

    [Fact]
    public async Task Running_again_resets_the_previous_results()
    {
        var status = 500;
        var sender = new FakeSender { Respond = (_, _) => Task.FromResult(Ok(status)) };
        var runner = Runner(sender, Item("a", "/a", StatusIs("200")));

        await runner.RunAsync();
        Assert.Equal(RunItemState.Failed, runner.Items[0].State);

        status = 200;
        await runner.RunAgainCommand.ExecuteAsync(null);

        Assert.Equal(RunItemState.Passed, runner.Items[0].State);
        Assert.Empty(runner.Items[0].Failures);
        Assert.False(runner.WasCancelled);
    }

    [Fact]
    public async Task Run_again_and_stop_are_only_available_at_the_right_time()
    {
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var sender = new FakeSender
        {
            Respond = async (_, _) =>
            {
                started.TrySetResult();
                await release.Task;
                return Ok(200);
            },
        };
        var runner = Runner(sender, Item("a", "/a"));

        Assert.False(runner.CancelCommand.CanExecute(null));

        var run = runner.RunAsync();
        await started.Task;

        Assert.True(runner.IsRunning);
        Assert.False(runner.RunAgainCommand.CanExecute(null));
        Assert.True(runner.CancelCommand.CanExecute(null));

        release.SetResult();
        await run;

        Assert.True(runner.RunAgainCommand.CanExecute(null));
        Assert.False(runner.CancelCommand.CanExecute(null));
    }
}
