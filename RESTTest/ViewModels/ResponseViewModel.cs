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

    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>Drives the status pill colour: 2xx green, 3xx blue, 4xx amber, 5xx red.</summary>
    public string StatusCategory => StatusCode switch
    {
        >= 200 and < 300 => "Success",
        >= 300 and < 400 => "Redirect",
        >= 400 and < 500 => "ClientError",
        >= 500 => "ServerError",
        _ => "Unknown",
    };

    public void Load(HttpExchangeResult result)
    {
        RequestUrl = result.Url;
        ElapsedText = FormatDuration(result.ElapsedMilliseconds);
        Headers.Clear();

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
        SizeText = FormatSize(result.ContentLength);
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
    }

    private static string FormatDuration(long milliseconds) =>
        milliseconds < 1000 ? $"{milliseconds} ms" : $"{milliseconds / 1000.0:0.00} s";

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.00} MB",
    };

    partial void OnStatusCodeChanged(int value) => OnPropertyChanged(nameof(StatusCategory));

    partial void OnErrorChanged(string? value) => OnPropertyChanged(nameof(HasError));
}
