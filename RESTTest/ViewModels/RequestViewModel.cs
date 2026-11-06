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
        [AuthKind.None, AuthKind.BearerToken, AuthKind.Basic, AuthKind.ApiKey];

    public static IReadOnlyList<ApiKeyLocation> ApiKeyLocations { get; } =
        [ApiKeyLocation.Header, ApiKeyLocation.Query];

    /// <summary>Body types in the order the picker shows them.</summary>
    public static IReadOnlyList<BodyKind> BodyKinds { get; } =
        [BodyKind.Json, BodyKind.Text, BodyKind.Xml, BodyKind.FormUrlEncoded, BodyKind.Multipart, BodyKind.None];

    /// <summary>Methods for which the body is actually transmitted.</summary>
    private static readonly HashSet<string> BodyCapableMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH", "DELETE",
    };

    [ObservableProperty]
    private string _method = "GET";

    [ObservableProperty]
    private string _path = string.Empty;

    /// <summary>Named BodyMode, not BodyKind: a property with its own type's name reads as a typo.</summary>
    [ObservableProperty]
    private BodyKind _bodyMode = BodyKind.Json;

    [ObservableProperty]
    private string _body = string.Empty;

    [ObservableProperty]
    private AuthKind _auth = AuthKind.None;

    [ObservableProperty]
    private string _bearerToken = string.Empty;

    [ObservableProperty]
    private string _basicUsername = string.Empty;

    [ObservableProperty]
    private string _basicPassword = string.Empty;

    [ObservableProperty]
    private string _apiKeyName = string.Empty;

    [ObservableProperty]
    private string _apiKeyValue = string.Empty;

    [ObservableProperty]
    private ApiKeyLocation _apiKeyPlacement = ApiKeyLocation.Header;

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
        _bodyMode = model.BodyKind;
        _body = model.Body;
        _auth = model.Auth;
        _bearerToken = model.BearerToken;
        _basicUsername = model.BasicUsername ?? string.Empty;
        _basicPassword = model.BasicPassword ?? string.Empty;
        _apiKeyName = model.ApiKeyName ?? string.Empty;
        _apiKeyValue = model.ApiKeyValue ?? string.Empty;
        _apiKeyPlacement = model.ApiKeyLocation;

        foreach (var parameter in model.QueryParams)
        {
            QueryParams.Add(new KeyValueViewModel(parameter));
        }

        foreach (var header in model.Headers)
        {
            Headers.Add(new KeyValueViewModel(header));
        }

        foreach (var field in model.FormFields ?? [])
        {
            FormFields.Add(new KeyValueViewModel(field));
        }

        foreach (var part in model.MultipartParts ?? [])
        {
            MultipartParts.Add(new MultipartPartViewModel(part));
        }

        foreach (var assertion in model.Assertions)
        {
            Assertions.Add(new AssertionViewModel(assertion));
        }

        Wire();
    }

    private void Wire()
    {
        TrackChildren(QueryParams);
        TrackChildren(Headers);
        TrackChildren(FormFields);
        TrackChildren(MultipartParts);
        TrackChildren(Assertions);

        // The tab headers show how many rows are actually in play. Dirty already fires for any
        // nested edit, and the count properties are transient, so this cannot recurse.
        Dirty += (_, _) =>
        {
            OnPropertyChanged(nameof(EnabledQueryParamCount));
            OnPropertyChanged(nameof(EnabledHeaderCount));
            OnPropertyChanged(nameof(EnabledAssertionCount));
        };
    }

    public ObservableCollection<KeyValueViewModel> QueryParams { get; } = [];

    public ObservableCollection<KeyValueViewModel> Headers { get; } = [];

    public ObservableCollection<KeyValueViewModel> FormFields { get; } = [];

    public ObservableCollection<MultipartPartViewModel> MultipartParts { get; } = [];

    public ObservableCollection<AssertionViewModel> Assertions { get; } = [];

    public IReadOnlyList<BodyKind> BodyOptions => BodyKinds;

    /// <summary>
    /// Which body editor the Body tab shows. Plain bools so the XAML needs no converters, and
    /// each is raised from <see cref="OnBodyModeChanged"/>.
    /// </summary>
    public bool IsJsonBody => BodyMode == BodyKind.Json;

    /// <summary>Text and XML share one editor: both are shown as plain, uncoloured text.</summary>
    public bool IsPlainTextBody => BodyMode is BodyKind.Text or BodyKind.Xml;

    public bool IsFormBody => BodyMode == BodyKind.FormUrlEncoded;

    public bool IsMultipartBody => BodyMode == BodyKind.Multipart;

    public bool IsNoBody => BodyMode == BodyKind.None;

    /// <summary>Format and Minify exist for the two structured text kinds.</summary>
    public bool CanFormatBody => BodyMode is BodyKind.Json or BodyKind.Xml;

    /// <summary>Instance views of the option lists, so XAML can bind them without x:Static.</summary>
    public IReadOnlyList<string> MethodOptions => Methods;

    public IReadOnlyList<AuthKind> AuthOptions => AuthKinds;

    public IReadOnlyList<ApiKeyLocation> ApiKeyLocationOptions => ApiKeyLocations;

    /// <summary>False for GET/HEAD/OPTIONS, used to warn that the body will not be sent.</summary>
    public bool SupportsBody => BodyCapableMethods.Contains(Method);

    /// <summary>Reveals the token box. A plain bool keeps the XAML free of chained converters.</summary>
    public bool IsBearerAuth => Auth == AuthKind.BearerToken;

    public bool IsBasicAuth => Auth == AuthKind.Basic;

    public bool IsApiKeyAuth => Auth == AuthKind.ApiKey;

    /// <summary>One line under the API key fields saying exactly how it will be sent.</summary>
    public string ApiKeyHint
    {
        get
        {
            var name = string.IsNullOrWhiteSpace(ApiKeyName) ? "<name>" : ApiKeyName.Trim();
            return ApiKeyPlacement == ApiKeyLocation.Query
                ? $"Sent as: ?{name}=<value> on the URL"
                : $"Sent as: {name}: <value>";
        }
    }

    public int EnabledQueryParamCount => QueryParams.Count(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Key));

    public int EnabledHeaderCount => Headers.Count(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Key));

    public int EnabledAssertionCount => Assertions.Count(a => a.Enabled);

    public RequestModel ToModel() => new()
    {
        Method = Method,
        Path = Path,
        BodyKind = BodyMode,
        Body = Body,
        FormFields = [.. FormFields.Select(f => f.ToModel())],
        MultipartParts = [.. MultipartParts.Select(p => p.ToModel())],
        Auth = Auth,
        BearerToken = BearerToken,
        BasicUsername = BasicUsername,
        BasicPassword = BasicPassword,
        ApiKeyName = ApiKeyName,
        ApiKeyValue = ApiKeyValue,
        ApiKeyLocation = ApiKeyPlacement,
        QueryParams = [.. QueryParams.Select(p => p.ToModel())],
        Headers = [.. Headers.Select(h => h.ToModel())],
        Assertions = [.. Assertions.Select(a => a.ToModel())],
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

    [RelayCommand]
    private void AddFormField() => FormFields.Add(new KeyValueViewModel());

    [RelayCommand]
    private void RemoveFormField(KeyValueViewModel? field)
    {
        if (field is not null)
        {
            FormFields.Remove(field);
        }
    }

    [RelayCommand]
    private void AddMultipartPart() => MultipartParts.Add(new MultipartPartViewModel());

    [RelayCommand]
    private void RemoveMultipartPart(MultipartPartViewModel? part)
    {
        if (part is not null)
        {
            MultipartParts.Remove(part);
        }
    }

    [RelayCommand]
    private void AddAssertion() => Assertions.Add(new AssertionViewModel());

    [RelayCommand]
    private void RemoveAssertion(AssertionViewModel? assertion)
    {
        if (assertion is not null)
        {
            Assertions.Remove(assertion);
        }
    }

    /// <summary>Re-indents the body, or reports where the JSON is malformed.</summary>
    [RelayCommand]
    private void FormatBody() => TransformBody(minify: false, done: "Formatted.");

    [RelayCommand]
    private void MinifyBody() => TransformBody(minify: true, done: "Minified.");

    /// <summary>Re-writes the body as JSON or XML depending on the body type, or reports why it cannot.</summary>
    private void TransformBody(bool minify, string done)
    {
        bool ok;
        string text;
        string? error;

        if (BodyMode == BodyKind.Xml)
        {
            ok = minify ? XmlText.TryMinify(Body, out text, out error) : XmlText.TryFormat(Body, out text, out error);
        }
        else
        {
            ok = minify ? JsonText.TryMinify(Body, out text, out error) : JsonText.TryFormat(Body, out text, out error);
        }

        if (ok)
        {
            Body = text;
            BodyStatusIsError = false;
            BodyStatus = done;
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
            or nameof(EnabledHeaderCount)
            or nameof(EnabledAssertionCount)
            or nameof(IsJsonBody)
            or nameof(IsPlainTextBody)
            or nameof(IsFormBody)
            or nameof(IsMultipartBody)
            or nameof(IsNoBody)
            or nameof(CanFormatBody)
            or nameof(IsBearerAuth)
            or nameof(IsBasicAuth)
            or nameof(IsApiKeyAuth)
            or nameof(ApiKeyHint);

    partial void OnMethodChanged(string value) => OnPropertyChanged(nameof(SupportsBody));

    partial void OnBodyModeChanged(BodyKind value)
    {
        OnPropertyChanged(nameof(IsJsonBody));
        OnPropertyChanged(nameof(IsPlainTextBody));
        OnPropertyChanged(nameof(IsFormBody));
        OnPropertyChanged(nameof(IsMultipartBody));
        OnPropertyChanged(nameof(IsNoBody));
        OnPropertyChanged(nameof(CanFormatBody));

        // A "Formatted." note about JSON means nothing once the body is something else.
        BodyStatus = null;
        BodyStatusIsError = false;
    }

    partial void OnAuthChanged(AuthKind value)
    {
        OnPropertyChanged(nameof(IsBearerAuth));
        OnPropertyChanged(nameof(IsBasicAuth));
        OnPropertyChanged(nameof(IsApiKeyAuth));
    }

    partial void OnApiKeyNameChanged(string value) => OnPropertyChanged(nameof(ApiKeyHint));

    partial void OnApiKeyPlacementChanged(ApiKeyLocation value) => OnPropertyChanged(nameof(ApiKeyHint));

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
