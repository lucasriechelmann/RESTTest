using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RESTTest.Models;
using RESTTest.Services;

namespace RESTTest.ViewModels;

public enum RunItemState
{
    Pending,
    Running,

    /// <summary>Completed and every enabled assertion held.</summary>
    Passed,

    /// <summary>Failed an assertion, or never got a response.</summary>
    Failed,

    /// <summary>Got a response but the request has no assertions, so nothing was checked.</summary>
    Unchecked,

    /// <summary>Not sent because the run was cancelled first.</summary>
    Skipped,
}

/// <summary>
/// One request's row in a runner report. Holds a snapshot of the request taken when the run
/// started, so editing the collection mid-run cannot change what is being sent.
/// </summary>
public partial class RunItemViewModel : ObservableObject
{
    [ObservableProperty]
    private RunItemState _state = RunItemState.Pending;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _elapsedText = string.Empty;

    public RunItemViewModel(string name, RequestModel request)
    {
        Name = name;
        Request = request;
    }

    public string Name { get; }

    public RequestModel Request { get; }

    public string Method => Request.Method;

    public string Path => Request.Path;

    /// <summary>The assertions that failed, with the reason, for display under the row.</summary>
    public ObservableCollection<string> Failures { get; } = [];

    public int AssertionsPassed { get; private set; }

    public int AssertionsTotal { get; private set; }

    public bool HasFailures => Failures.Count > 0;

    public void Reset()
    {
        State = RunItemState.Pending;
        StatusText = string.Empty;
        ElapsedText = string.Empty;
        Failures.Clear();
        AssertionsPassed = 0;
        AssertionsTotal = 0;
        OnPropertyChanged(nameof(HasFailures));
    }

    public void Apply(HttpExchangeResult result, IReadOnlyList<AssertionResult> tests)
    {
        ElapsedText = result.ElapsedMilliseconds < 1000
            ? $"{result.ElapsedMilliseconds} ms"
            : $"{result.ElapsedMilliseconds / 1000.0:0.00} s";

        StatusText = result.Completed
            ? $"{result.StatusCode} {result.ReasonPhrase}".Trim()
            : "No response";

        AssertionsTotal = tests.Count;
        AssertionsPassed = tests.Count(t => t.Passed);

        Failures.Clear();

        if (!result.Completed)
        {
            // Surfaced once here; the per-assertion "no response" messages would only repeat it.
            Failures.Add(result.Error ?? "The request failed.");
            State = RunItemState.Failed;
        }
        else
        {
            foreach (var failed in tests.Where(t => !t.Passed))
            {
                Failures.Add(failed.Message);
            }

            State = tests.Count == 0
                ? RunItemState.Unchecked
                : Failures.Count == 0 ? RunItemState.Passed : RunItemState.Failed;
        }

        OnPropertyChanged(nameof(HasFailures));
    }
}
