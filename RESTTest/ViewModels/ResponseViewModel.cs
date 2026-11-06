using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>True when the body is not text; the viewer then shows <see cref="BinaryNotice"/> instead.</summary>
    [ObservableProperty]
    private bool _isBinary;

    [ObservableProperty]
    private string _binaryNotice = string.Empty;

    /// <summary>"Saved 12 KB to C:\...": feedback under the status row after a successful save.</summary>
    [ObservableProperty]
    private string _saveNote = string.Empty;

    /// <summary>
    /// A JSONPath typed above the Preview ("$.items[*].id"). While it is valid the Preview shows only
    /// what it selects. Kept across re-sends of the same request, cleared when another request is selected.
    /// </summary>
    [ObservableProperty]
    private string _filterText = string.Empty;

    /// <summary>What the Preview shows: the pretty body, or just the part <see cref="FilterText"/> selects.</summary>
    [ObservableProperty]
    private string _displayBody = string.Empty;

    /// <summary>"3 matches", "No matches", or why the path could not be used.</summary>
    [ObservableProperty]
    private string _filterStatus = string.Empty;

    [ObservableProperty]
    private bool _filterHasError;

    /// <summary>Show every line in the Diff tab instead of just the changes and their surroundings.</summary>
    [ObservableProperty]
    private bool _showAllLines;

    /// <summary>"Compared with the previous response (12:31:02)": says what the Diff tab is showing.</summary>
    [ObservableProperty]
    private string _diffNote = string.Empty;

    /// <summary>"Status 200 OK → 404 Not Found · +3 -1": the changes at a glance.</summary>
    [ObservableProperty]
    private string _diffSummary = string.Empty;

    private HttpExchangeResult? _current;
    private ResponseBaseline? _baseline;
    private DiffResult? _diff;

    private readonly IDialogService? _dialogs;
    private byte[] _bodyBytes = [];
    private string _contentType = string.Empty;
    private string? _contentDisposition;

    public ResponseViewModel()
    {
    }

    public ResponseViewModel(IDialogService dialogs)
    {
        _dialogs = dialogs;
    }

    public ObservableCollection<KeyValueModel> Headers { get; } = [];

    /// <summary>The lines shown in the Diff tab (changes with context, or everything).</summary>
    public ObservableCollection<DiffLine> DiffLines { get; } = [];

    /// <summary>The filter box only makes sense for a received, textual response.</summary>
    public bool CanFilter => HasResponse && !HasError && !IsBinary;

    partial void OnFilterTextChanged(string value) => RefreshDisplay();

    /// <summary>Recomputes <see cref="DisplayBody"/> and the filter note from the body and the filter text.</summary>
    private void RefreshDisplay()
    {
        OnPropertyChanged(nameof(CanFilter));

        if (!CanFilter || string.IsNullOrWhiteSpace(FilterText))
        {
            DisplayBody = PrettyBody;
            FilterStatus = string.Empty;
            FilterHasError = false;
            return;
        }

        var result = JsonPathQuery.Run(RawBody, FilterText);

        if (!result.Ok)
        {
            // Half-typed paths are normal while typing: keep the whole body in view and say what is wrong.
            DisplayBody = PrettyBody;
            FilterStatus = result.Error ?? string.Empty;
            FilterHasError = true;
            return;
        }

        FilterHasError = false;
        FilterStatus = result.Summary;
        DisplayBody = result.Matches.Count == 0 ? string.Empty : JsonText.PrettyOrOriginal(result.ToDisplayJson());
    }

    public bool HasBaseline => _baseline is not null;

    public bool IsPinned => _baseline?.Pinned == true;

    /// <summary>Whether there is a text diff to list; binary and too-large bodies show only the summary.</summary>
    public bool HasDiffLines => DiffLines.Count > 0;

    /// <summary>A response can be pinned once one has arrived. Set by <see cref="MainViewModel"/>, which owns the per-request memory.</summary>
    public Action? PinHandler { get; set; }

    public Action? UnpinHandler { get; set; }

    public bool CanPin => _current is not null;

    [RelayCommand(CanExecute = nameof(CanPin))]
    private void PinBaseline() => PinHandler?.Invoke();

    [RelayCommand]
    private void UnpinBaseline() => UnpinHandler?.Invoke();

    /// <summary>
    /// Compares the response now showing with <paramref name="baseline"/> (null clears the comparison).
    /// Called by <see cref="Load"/>, and again when the user pins or unpins.
    /// </summary>
    public void SetBaseline(ResponseBaseline? baseline)
    {
        _baseline = baseline;
        RebuildDiff();
    }

    private void RebuildDiff()
    {
        _diff = null;
        DiffLines.Clear();
        DiffSummary = string.Empty;
        DiffNote = string.Empty;

        if (_baseline is { } baseline && _current is { } current)
        {
            DiffNote = baseline.Pinned
                ? $"Compared with the pinned baseline (received {baseline.When:HH:mm:ss})"
                : $"Compared with the previous response (received {baseline.When:HH:mm:ss})";

            var changes = new List<string>();
            var before = baseline.Result;

            if (before.StatusCode != current.StatusCode || before.ReasonPhrase != current.ReasonPhrase)
            {
                changes.Add($"Status {before.StatusCode} {before.ReasonPhrase} → {current.StatusCode} {current.ReasonPhrase}".Replace("  ", " "));
            }

            if (before.IsBinary || current.IsBinary)
            {
                changes.Add(before.BodyBytes.AsSpan().SequenceEqual(current.BodyBytes)
                    ? "Binary body: identical"
                    : $"Binary body: differs ({DisplayFormat.Size(before.BodyBytes.Length)} → {DisplayFormat.Size(current.BodyBytes.Length)})");
            }
            else
            {
                // Both sides pretty-printed the same way, so a reformatted body is not reported as a change.
                _diff = TextDiff.Compare(JsonText.PrettyOrOriginal(before.Body), JsonText.PrettyOrOriginal(current.Body));
                changes.Add(_diff.IsIdentical ? "Body: no differences" : $"Body: {_diff.Summary}");
            }

            DiffSummary = string.Join(" · ", changes);
        }

        RefreshDiffLines();
        OnPropertyChanged(nameof(HasBaseline));
        OnPropertyChanged(nameof(IsPinned));
        OnPropertyChanged(nameof(CanPin));
        PinBaselineCommand.NotifyCanExecuteChanged();
    }

    private void RefreshDiffLines()
    {
        DiffLines.Clear();

        if (_diff is { TooLarge: false, IsIdentical: false })
        {
            foreach (var line in ShowAllLines ? _diff.Lines : _diff.WithContext())
            {
                DiffLines.Add(line);
            }
        }

        OnPropertyChanged(nameof(HasDiffLines));
    }

    partial void OnShowAllLinesChanged(bool value) => RefreshDiffLines();

    /// <summary>Saving needs a received, non-empty body; an error or an empty 204 has nothing to write.</summary>
    public bool CanSave => HasResponse && !HasError && _bodyBytes.Length > 0;

    /// <summary>The name the save dialog proposes: the server's, else the URL's, else a typed default.</summary>
    public string SuggestedFileName =>
        ResponseFileName.Suggest(RequestUrl, _contentType, _contentDisposition, IsBinary);

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

    /// <param name="baseline">The earlier response to compare this one with in the Diff tab, or null for none.</param>
    public void Load(HttpExchangeResult result, IReadOnlyList<AssertionResult>? tests = null, ResponseBaseline? baseline = null)
    {
        RequestUrl = result.Url;
        ElapsedText = DisplayFormat.Duration(result.ElapsedMilliseconds);
        Headers.Clear();
        LoadTests(tests);
        SaveNote = string.Empty;

        if (!result.Completed)
        {
            _bodyBytes = [];
            IsBinary = false;
            BinaryNotice = string.Empty;
            HasResponse = true;
            Error = result.Error;
            StatusCode = 0;
            StatusText = "No response";
            SizeText = string.Empty;
            PrettyBody = string.Empty;
            RawBody = string.Empty;
            _current = null;
            SetBaseline(null);
            RaiseSaveChanged();
            RefreshDisplay();
            return;
        }

        Error = null;
        StatusCode = result.StatusCode;
        StatusText = $"{result.StatusCode} {result.ReasonPhrase}".Trim();
        SizeText = DisplayFormat.Size(result.ContentLength);
        RawBody = result.Body;
        PrettyBody = JsonText.PrettyOrOriginal(result.Body);

        // Results built by hand (a test, a fake sender) may carry only text: write that as UTF-8.
        _bodyBytes = result.BodyBytes.Length > 0 || result.Body.Length == 0
            ? result.BodyBytes
            : Encoding.UTF8.GetBytes(result.Body);
        _contentType = result.ContentType;
        IsBinary = result.IsBinary;
        BinaryNotice = result.IsBinary
            ? $"Binary content ({DisplayFormat.Size(_bodyBytes.Length)}, {(_contentType.Length == 0 ? "unknown type" : _contentType)}). " +
              "It is not shown as text. Use Save to write it to a file."
            : string.Empty;

        foreach (var header in result.ResponseHeaders)
        {
            Headers.Add(header);
        }

        _contentDisposition = result.ResponseHeaders
            .FirstOrDefault(h => h.Key.Equals("Content-Disposition", StringComparison.OrdinalIgnoreCase))?.Value;

        HasResponse = true;
        RaiseSaveChanged();
        RefreshDisplay();

        _current = result;
        SetBaseline(baseline);
    }

    /// <summary>Writes the body to a file the user picks, byte for byte, so a download is not altered by text decoding.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void SaveToFile()
    {
        if (_dialogs is null)
        {
            return;
        }

        var extension = Path.GetExtension(SuggestedFileName);
        var filter = extension.Length > 0
            ? $"{extension.TrimStart('.').ToUpperInvariant()} files (*{extension})|*{extension}|All files (*.*)|*.*"
            : "All files (*.*)|*.*";

        if (_dialogs.PickSavePath("Save response", SuggestedFileName, filter) is not { } path)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(path, _bodyBytes);
            SaveNote = $"Saved {DisplayFormat.Size(_bodyBytes.Length)} to {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveNote = string.Empty;
            _dialogs.ShowMessage("Could not save", $"The response could not be written: {ex.Message}");
        }
    }

    private void RaiseSaveChanged()
    {
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(SuggestedFileName));
        SaveToFileCommand.NotifyCanExecuteChanged();
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
        _bodyBytes = [];
        IsBinary = false;
        BinaryNotice = string.Empty;
        SaveNote = string.Empty;
        RaiseSaveChanged();
        _current = null;
        SetBaseline(null);

        // A filter belongs to the request it was typed for: another request starts unfiltered.
        FilterText = string.Empty;
        RefreshDisplay();
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
