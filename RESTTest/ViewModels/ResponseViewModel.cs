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

    public void Load(HttpExchangeResult result, IReadOnlyList<AssertionResult>? tests = null)
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
            RaiseSaveChanged();
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
