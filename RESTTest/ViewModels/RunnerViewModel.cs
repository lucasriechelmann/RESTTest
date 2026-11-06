using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RESTTest.Services;

namespace RESTTest.ViewModels;

/// <summary>
/// Runs a batch of requests one after another against a fixed base URL and reports which
/// assertions held. Runtime state only: like <see cref="ResponseViewModel"/> it is not an
/// <see cref="EditableViewModel"/>, so a run can never mark a collection dirty.
/// </summary>
public partial class RunnerViewModel : ObservableObject
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(120);

    private readonly IHttpRequestSender _sender;
    private readonly string? _baseUrl;

    private CancellationTokenSource? _cancellation;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunAgainCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private int _completedCount;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private bool _wasCancelled;

    public RunnerViewModel(IHttpRequestSender sender, string? baseUrl, string title, IEnumerable<RunItemViewModel> items)
    {
        _sender = sender;
        _baseUrl = baseUrl;
        Title = title;

        foreach (var item in items)
        {
            Items.Add(item);
        }
    }

    /// <summary>What was run, e.g. "Sample API / Posts".</summary>
    public string Title { get; }

    /// <summary>The base URL the whole run targets, shown so the report says what it hit.</summary>
    public string BaseUrlText => string.IsNullOrWhiteSpace(_baseUrl) ? "(no base URL)" : _baseUrl;

    public ObservableCollection<RunItemViewModel> Items { get; } = [];

    public int TotalCount => Items.Count;

    /// <summary>True when the run finished, was not cancelled, and nothing failed.</summary>
    public bool AllPassed => !IsRunning && !WasCancelled && CompletedCount == TotalCount
                             && Items.All(i => i.State is RunItemState.Passed or RunItemState.Unchecked);

    public bool HasFailures => Items.Any(i => i.State == RunItemState.Failed);

    /// <summary>
    /// Runs every request in order. Never throws for request problems: a dead server or a failed
    /// assertion is data on the row, the same way <see cref="IHttpRequestSender"/> reports it.
    /// </summary>
    public async Task RunAsync()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        WasCancelled = false;
        CompletedCount = 0;
        Summary = string.Empty;

        foreach (var item in Items)
        {
            item.Reset();
        }

        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        var runToken = _cancellation.Token;

        try
        {
            foreach (var item in Items)
            {
                if (runToken.IsCancellationRequested)
                {
                    item.State = RunItemState.Skipped;
                    continue;
                }

                item.State = RunItemState.Running;

                // Each request gets its own timeout, linked to the run so Cancel still stops it.
                using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(runToken);
                requestCancellation.CancelAfter(RequestTimeout);

                var result = await _sender
                    .SendAsync(_baseUrl, item.Request, requestCancellation.Token)
                    .ConfigureAwait(true);

                if (runToken.IsCancellationRequested)
                {
                    // The in-flight request was cut short by Cancel; its "failure" is ours, not the server's.
                    item.State = RunItemState.Skipped;
                    continue;
                }

                item.Apply(result, AssertionEvaluator.EvaluateAll(item.Request.Assertions, result));
                CompletedCount++;
            }
        }
        finally
        {
            WasCancelled = runToken.IsCancellationRequested;
            IsRunning = false;
            Summary = BuildSummary();
            OnPropertyChanged(nameof(AllPassed));
            OnPropertyChanged(nameof(HasFailures));
        }
    }

    private bool CanRunAgain() => !IsRunning;

    private bool CanCancel() => IsRunning;

    [RelayCommand(CanExecute = nameof(CanRunAgain))]
    private Task RunAgainAsync() => RunAsync();

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cancellation?.Cancel();

    private string BuildSummary()
    {
        var passed = Items.Count(i => i.State == RunItemState.Passed);
        var failed = Items.Count(i => i.State == RunItemState.Failed);
        var unchecked_ = Items.Count(i => i.State == RunItemState.Unchecked);
        var skipped = Items.Count(i => i.State == RunItemState.Skipped);
        var assertionsPassed = Items.Sum(i => i.AssertionsPassed);
        var assertionsTotal = Items.Sum(i => i.AssertionsTotal);

        var parts = new List<string> { $"{passed} passed", $"{failed} failed" };

        if (unchecked_ > 0)
        {
            parts.Add($"{unchecked_} without tests");
        }

        if (skipped > 0)
        {
            parts.Add($"{skipped} skipped");
        }

        return $"{string.Join(" · ", parts)} · {assertionsPassed}/{assertionsTotal} assertions";
    }

    partial void OnCompletedCountChanged(int value) => OnPropertyChanged(nameof(AllPassed));

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(AllPassed));
}
