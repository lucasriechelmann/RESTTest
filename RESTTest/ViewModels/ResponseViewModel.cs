using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RESTTest.Models;
using RESTTest.Services;

namespace RESTTest.ViewModels;

/// <summary>
/// Read-only view of the last exchange. Deliberately not an <see cref="EditableViewModel"/>:
/// a response is never persisted, so it must not be able to mark the collection dirty.
/// </summary>
public partial class ResponseViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _hasResponse;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private int _statusCode;

    [ObservableProperty]
    private string _elapsedText = string.Empty;

    [ObservableProperty]
    private string _sizeText = string.Empty;

    [ObservableProperty]
    private string _prettyBody = string.Empty;

    [ObservableProperty]
    private string _rawBody = string.Empty;

    [ObservableProperty]
    private string _requestUrl = string.Empty;

    [ObservableProperty]
    private string? _error;

    public ObservableCollection<KeyValueModel> Headers { get; } = [];

    /// <summary>Outcome of the request's assertions for this response. Empty when it has none.</summary>
    public ObservableCollection<AssertionResult> TestResults { get; } = [];

    public bool HasError => !string.IsNullOrEmpty(Error);

    public bool HasTests => TestResults.Count > 0;

    public bool AllTestsPassed => TestResults.All(t => t.Passed);

    /// <summary>"3/4 tests passed", shown next to the status pill.</summary>
    public string TestSummary => HasTests
        ? $"{TestResults.Count(t => t.Passed)}/{TestResults.Count} tests passed"
        : string.Empty;

    /// <summary>Drives the status pill colour: 2xx green, 3xx blue, 4xx amber, 5xx red.</summary>
    public string StatusCategory => DisplayFormat.StatusCategory(StatusCode);

    public void Load(HttpExchangeResult result, IReadOnlyList<AssertionResult>? tests = null)
    {
        RequestUrl = result.Url;
        ElapsedText = DisplayFormat.Duration(result.ElapsedMilliseconds);
        Headers.Clear();
        LoadTests(tests);

        if (!result.Completed)
        {
            HasResponse = true;
            Error = result.Error;
            StatusCode = 0;
            StatusText = "No response";
            SizeText = string.Empty;
            PrettyBody = string.Empty;
            RawBody = string.Empty;
            return;
        }

        Error = null;
        StatusCode = result.StatusCode;
        StatusText = $"{result.StatusCode} {result.ReasonPhrase}".Trim();
        SizeText = DisplayFormat.Size(result.ContentLength);
        RawBody = result.Body;
        PrettyBody = JsonText.PrettyOrOriginal(result.Body);

        foreach (var header in result.ResponseHeaders)
        {
            Headers.Add(header);
        }

        HasResponse = true;
    }

    public void Clear()
    {
        HasResponse = false;
        Error = null;
        StatusCode = 0;
        StatusText = string.Empty;
        ElapsedText = string.Empty;
        SizeText = string.Empty;
        PrettyBody = string.Empty;
        RawBody = string.Empty;
        RequestUrl = string.Empty;
        Headers.Clear();
        LoadTests(null);
    }

    private void LoadTests(IReadOnlyList<AssertionResult>? tests)
    {
        TestResults.Clear();

        foreach (var test in tests ?? [])
        {
            TestResults.Add(test);
        }

        OnPropertyChanged(nameof(HasTests));
        OnPropertyChanged(nameof(AllTestsPassed));
        OnPropertyChanged(nameof(TestSummary));
    }

    partial void OnStatusCodeChanged(int value) => OnPropertyChanged(nameof(StatusCategory));

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));
}
