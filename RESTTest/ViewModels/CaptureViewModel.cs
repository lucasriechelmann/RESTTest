using CommunityToolkit.Mvvm.ComponentModel;
using RESTTest.Models;
using RESTTest.Services;

namespace RESTTest.ViewModels;

/// <summary>
/// One editable row in a request's Captures tab: "after a send, save this part of the response as
/// this name". The visibility flag and placeholder derive from <see cref="Source"/> and are transient.
/// </summary>
public partial class CaptureViewModel : EditableViewModel
{
    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private AssertionTarget _source = AssertionTarget.JsonPath;

    /// <summary>The header name or JSON path (not "Property": see <see cref="AssertionViewModel.Selector"/>).</summary>
    [ObservableProperty]
    private string _selector = string.Empty;

    public CaptureViewModel()
    {
    }

    public CaptureViewModel(CaptureModel model)
    {
        _enabled = model.Enabled;
        _name = model.Name;
        _source = CaptureEvaluator.Sources.Contains(model.Source) ? model.Source : AssertionTarget.JsonPath;
        _selector = model.Selector;
    }

    /// <summary>Instance view of the static list, so XAML can bind it without x:Static.</summary>
    public IReadOnlyList<AssertionTarget> SourceOptions => CaptureEvaluator.Sources;

    public bool ShowsSelector => Source is AssertionTarget.Header or AssertionTarget.JsonPath;

    public string SelectorPlaceholder => Source == AssertionTarget.Header ? "Location" : "$.token";

    public CaptureModel ToModel() => new()
    {
        Enabled = Enabled,
        Name = Name.Trim(),
        Source = Source,
        Selector = Selector,
    };

    protected override bool IsTransient(string? propertyName) =>
        propertyName is nameof(ShowsSelector) or nameof(SelectorPlaceholder);

    partial void OnSourceChanged(AssertionTarget value)
    {
        OnPropertyChanged(nameof(ShowsSelector));
        OnPropertyChanged(nameof(SelectorPlaceholder));
    }
}
