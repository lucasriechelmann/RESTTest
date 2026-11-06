using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RESTTest.Models;
using RESTTest.Services;

namespace RESTTest.ViewModels;

/// <summary>
/// The editable request. Note there is no host here by design: the request owns the method and
/// the path, and the collection's selected environment supplies the base URL.
/// </summary>
public partial class RequestViewModel : EditableViewModel
{
    /// <summary>Methods offered in the combo box, in the order they are shown.</summary>
    public static IReadOnlyList<string> Methods { get; } =
        ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"];

    public static IReadOnlyList<AuthKind> AuthKinds { get; } =
        [AuthKind.None, AuthKind.BearerToken];

    /// <summary>Methods for which the body is actually transmitted.</summary>
    private static readonly HashSet<string> BodyCapableMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH", "DELETE",
    };

    [ObservableProperty]
    private string _method = "GET";

    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private string _body = string.Empty;

    [ObservableProperty]
    private AuthKind _auth = AuthKind.None;

    [ObservableProperty]
    private string _bearerToken = string.Empty;

    /// <summary>Result of the last format attempt, shown under the body editor.</summary>
    [ObservableProperty]
    private string? _bodyStatus;

    [ObservableProperty]
    private bool _bodyStatusIsError;

    public RequestViewModel()
    {
        Wire();
    }

    public RequestViewModel(RequestModel model)
    {
        _method = string.IsNullOrWhiteSpace(model.Method) ? "GET" : model.Method;
        _path = model.Path;
        _body = model.Body;
        _auth = model.Auth;
        _bearerToken = model.BearerToken;

        foreach (var parameter in model.QueryParams)
        {
            QueryParams.Add(new KeyValueViewModel(parameter));
        }

        foreach (var header in model.Headers)
        {
            Headers.Add(new KeyValueViewModel(header));
        }

        Wire();
    }

    private void Wire()
    {
        TrackChildren(QueryParams);
        TrackChildren(Headers);

        // The tab headers show how many rows are actually in play. Dirty already fires for any
        // nested edit, and the count properties are transient, so this cannot recurse.
        Dirty += (_, _) =>
        {
            OnPropertyChanged(nameof(EnabledQueryParamCount));
            OnPropertyChanged(nameof(EnabledHeaderCount));
        };
    }

    public ObservableCollection<KeyValueViewModel> QueryParams { get; } = [];

    public ObservableCollection<KeyValueViewModel> Headers { get; } = [];

    /// <summary>Instance views of the option lists, so XAML can bind them without x:Static.</summary>
    public IReadOnlyList<string> MethodOptions => Methods;

    public IReadOnlyList<AuthKind> AuthOptions => AuthKinds;

    /// <summary>False for GET/HEAD/OPTIONS, used to warn that the body will not be sent.</summary>
    public bool SupportsBody => BodyCapableMethods.Contains(Method);

    /// <summary>Reveals the token box. A plain bool keeps the XAML free of chained converters.</summary>
    public bool IsBearerAuth => Auth == AuthKind.BearerToken;

    public int EnabledQueryParamCount => QueryParams.Count(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Key));

    public int EnabledHeaderCount => Headers.Count(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Key));

    public RequestModel ToModel() => new()
    {
        Method = Method,
        Path = Path,
        Body = Body,
        Auth = Auth,
        BearerToken = BearerToken,
        QueryParams = [.. QueryParams.Select(p => p.ToModel())],
        Headers = [.. Headers.Select(h => h.ToModel())],
    };

    [RelayCommand]
    private void AddQueryParam() => QueryParams.Add(new KeyValueViewModel());

    [RelayCommand]
    private void RemoveQueryParam(KeyValueViewModel? parameter)
    {
        if (parameter is not null)
        {
            QueryParams.Remove(parameter);
        }
    }

    [RelayCommand]
    private void AddHeader() => Headers.Add(new KeyValueViewModel());

    [RelayCommand]
    private void RemoveHeader(KeyValueViewModel? header)
    {
        if (header is not null)
        {
            Headers.Remove(header);
        }
    }

    /// <summary>Re-indents the body, or reports where the JSON is malformed.</summary>
    [RelayCommand]
    private void FormatBody()
    {
        if (JsonText.TryFormat(Body, out var formatted, out var error))
        {
            Body = formatted;
            BodyStatusIsError = false;
            BodyStatus = "Formatted.";
        }
        else
        {
            BodyStatusIsError = true;
            BodyStatus = error;
        }
    }

    [RelayCommand]
    private void MinifyBody()
    {
        if (JsonText.TryMinify(Body, out var minified, out var error))
        {
            Body = minified;
            BodyStatusIsError = false;
            BodyStatus = "Minified.";
        }
        else
        {
            BodyStatusIsError = true;
            BodyStatus = error;
        }
    }

    protected override bool IsTransient(string? propertyName) =>
        propertyName is nameof(BodyStatus)
            or nameof(BodyStatusIsError)
            or nameof(EnabledQueryParamCount)
            or nameof(EnabledHeaderCount);

    partial void OnMethodChanged(string value) => OnPropertyChanged(nameof(SupportsBody));

    partial void OnAuthChanged(AuthKind value) => OnPropertyChanged(nameof(IsBearerAuth));

    partial void OnBodyChanged(string value)
    {
        // Clear a stale "Formatted." / error note as soon as the text is touched again.
        if (BodyStatus is not null)
        {
            BodyStatus = null;
            BodyStatusIsError = false;
        }
    }
}
