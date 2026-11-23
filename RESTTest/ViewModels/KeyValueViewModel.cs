using CommunityToolkit.Mvvm.ComponentModel;
using RESTTest.Models;

namespace RESTTest.ViewModels;

/// <summary>
/// One editable row in the query-parameter, header, form-field or path-parameter table. The value
/// is either typed or taken from a capture (<see cref="UsesCapture"/>); the typed text is kept
/// while a capture is used, so switching back loses nothing.
/// </summary>
public partial class KeyValueViewModel : EditableViewModel
{
    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;

    /// <summary>True when the value comes from the collection's captured value named <see cref="CaptureName"/>.</summary>
    [ObservableProperty]
    private bool _usesCapture;

    [ObservableProperty]
    private string _captureName = string.Empty;

    public KeyValueViewModel()
    {
    }

    public KeyValueViewModel(KeyValueModel model)
    {
        _enabled = model.Enabled;
        _key = model.Key;
        _value = model.Value;
        _usesCapture = model.Capture is not null;
        _captureName = model.Capture ?? string.Empty;
    }

    public KeyValueModel ToModel() => new()
    {
        Enabled = Enabled,
        Key = Key,
        Value = Value,
        Capture = UsesCapture ? CaptureName.Trim() : null,
    };
}
