using CommunityToolkit.Mvvm.ComponentModel;
using RESTTest.Models;

namespace RESTTest.ViewModels;

/// <summary>One editable row in the query-parameter or header table.</summary>
public partial class KeyValueViewModel : EditableViewModel
{
    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private string _value = string.Empty;

    public KeyValueViewModel()
    {
    }

    public KeyValueViewModel(KeyValueModel model)
    {
        _enabled = model.Enabled;
        _key = model.Key;
        _value = model.Value;
    }

    public KeyValueModel ToModel() => new()
    {
        Enabled = Enabled,
        Key = Key,
        Value = Value,
    };
}
