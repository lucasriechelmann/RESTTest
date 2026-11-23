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

    // Each secret can come from a capture instead of being typed (a login's token, say). The typed
    // text is kept while a capture is used, so switching back loses nothing.
    [ObservableProperty]
    private bool _bearerTokenUsesCapture;

    [ObservableProperty]
    private string _bearerTokenCapture = string.Empty;

    [ObservableProperty]
    private bool _basicPasswordUsesCapture;

    [ObservableProperty]
    private string _basicPasswordCapture = string.Empty;

    [ObservableProperty]
    private bool _apiKeyValueUsesCapture;

    [ObservableProperty]
    private string _apiKeyValueCapture = string.Empty;

    /// <summary>Seconds before a send is abandoned; 0 means no limit. Kept between 0 and an hour.</summary>
    [ObservableProperty]
    private int _timeoutSeconds = RequestModel.DefaultTimeoutSeconds;

    [ObservableProperty]
    private bool _followRedirects = true;

    [ObservableProperty]
    private bool _verifySsl = true;

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
        _bearerTokenUsesCapture = model.BearerTokenCapture is not null;
        _bearerTokenCapture = model.BearerTokenCapture ?? string.Empty;
        _basicPasswordUsesCapture = model.BasicPasswordCapture is not null;
        _basicPasswordCapture = model.BasicPasswordCapture ?? string.Empty;
        _apiKeyValueUsesCapture = model.ApiKeyValueCapture is not null;
        _apiKeyValueCapture = model.ApiKeyValueCapture ?? string.Empty;
        _timeoutSeconds = Math.Clamp(model.TimeoutSeconds, 0, RequestModel.MaxTimeoutSeconds);
        _followRedirects = model.FollowRedirects;
        _verifySsl = model.VerifySsl;

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

        foreach (var capture in model.Captures ?? [])
        {
            Captures.Add(new CaptureViewModel(capture));
        }

        foreach (var parameter in model.PathParams ?? [])
        {
            PathParams.Add(new KeyValueViewModel(parameter));
        }

        // Before Wire: a file saved before path parameters existed (or an imported "/users/:id")
        // gets its rows here, and building them is not an edit.
        SyncPathParams();

        Wire();
    }

    private void Wire()
    {
        TrackChildren(PathParams);
        TrackChildren(Captures);
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
            OnPropertyChanged(nameof(EnabledCaptureCount));
        };
    }

    /// <summary>
    /// Path parameter rows removed because their segment left the path, kept by name for this
    /// session: typing ":i", ":id" would otherwise lose a value typed for "id" on the way.
    /// </summary>
    private readonly Dictionary<string, KeyValueViewModel> _parkedPathParams = new(StringComparer.Ordinal);

    /// <summary>One row per <c>:name</c> segment of the path, in path order; values follow the name.</summary>
    public ObservableCollection<KeyValueViewModel> PathParams { get; } = [];

    public bool HasPathParams => PathParams.Count > 0;

    public ObservableCollection<CaptureViewModel> Captures { get; } = [];

    public int EnabledCaptureCount => Captures.Count(c => c.Enabled && !string.IsNullOrWhiteSpace(c.Name));

    /// <summary>Makes <see cref="PathParams"/> match the <c>:name</c> segments of <see cref="Path"/>.</summary>
    private void SyncPathParams()
    {
        var names = PathParameters.NamesIn(Path);
        if (names.SequenceEqual(PathParams.Select(p => p.Key), StringComparer.Ordinal))
        {
            return;
        }

        var existing = PathParams.ToDictionary(p => p.Key, StringComparer.Ordinal);
        foreach (var row in PathParams)
        {
            if (!names.Contains(row.Key, StringComparer.Ordinal))
            {
                _parkedPathParams[row.Key] = row;
            }
        }

        var wanted = names
            .Select(name => existing.GetValueOrDefault(name)
                            ?? (_parkedPathParams.Remove(name, out var parked) ? parked : new KeyValueViewModel { Key = name }))
            .ToList();

        // Rebuilt in place so the same ObservableCollection (and its Dirty subscription) stays bound.
        PathParams.Clear();
        foreach (var row in wanted)
        {
            PathParams.Add(row);
        }

        OnPropertyChanged(nameof(HasPathParams));
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

    /// <summary>True when any send setting differs from the default; marks the Settings tab header.</summary>
    public bool HasCustomSettings =>
        TimeoutSeconds != RequestModel.DefaultTimeoutSeconds || !FollowRedirects || !VerifySsl;

    /// <summary>One line under the timeout box that says what the number means.</summary>
    public string TimeoutHint => TimeoutSeconds == 0
        ? "No time limit: the request waits until the server answers or you press Cancel."
        : $"The request is abandoned after {TimeoutSeconds} second{(TimeoutSeconds == 1 ? string.Empty : "s")} (whole exchange, body included).";

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
        BearerTokenCapture = BearerTokenUsesCapture ? BearerTokenCapture.Trim() : null,
        BasicPasswordCapture = BasicPasswordUsesCapture ? BasicPasswordCapture.Trim() : null,
        ApiKeyValueCapture = ApiKeyValueUsesCapture ? ApiKeyValueCapture.Trim() : null,
        PathParams = [.. PathParams.Select(p => p.ToModel())],
        Captures = [.. Captures.Select(c => c.ToModel())],
        TimeoutSeconds = TimeoutSeconds,
        FollowRedirects = FollowRedirects,
        VerifySsl = VerifySsl,
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
    private void AddCapture() => Captures.Add(new CaptureViewModel());

    [RelayCommand]
    private void RemoveCapture(CaptureViewModel? capture)
    {
        if (capture is not null)
        {
            Captures.Remove(capture);
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
            or nameof(EnabledCaptureCount)
            or nameof(HasPathParams)
            or nameof(IsJsonBody)
            or nameof(IsPlainTextBody)
            or nameof(IsFormBody)
            or nameof(IsMultipartBody)
            or nameof(IsNoBody)
            or nameof(CanFormatBody)
            or nameof(IsBearerAuth)
            or nameof(IsBasicAuth)
            or nameof(IsApiKeyAuth)
            or nameof(ApiKeyHint)
            or nameof(HasCustomSettings)
            or nameof(TimeoutHint);

    partial void OnMethodChanged(string value) => OnPropertyChanged(nameof(SupportsBody));

    partial void OnPathChanged(string value) => SyncPathParams();

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

    partial void OnTimeoutSecondsChanged(int value)
    {
        // A typed 99999 or -5 snaps to the nearest allowed value instead of being stored.
        var clamped = Math.Clamp(value, 0, RequestModel.MaxTimeoutSeconds);
        if (clamped != value)
        {
            TimeoutSeconds = clamped;
            return;
        }

        OnPropertyChanged(nameof(HasCustomSettings));
        OnPropertyChanged(nameof(TimeoutHint));
    }

    partial void OnFollowRedirectsChanged(bool value) => OnPropertyChanged(nameof(HasCustomSettings));

    partial void OnVerifySslChanged(bool value) => OnPropertyChanged(nameof(HasCustomSettings));

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
