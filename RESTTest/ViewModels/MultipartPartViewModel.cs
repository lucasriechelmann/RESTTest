using CommunityToolkit.Mvvm.ComponentModel;
using RESTTest.Models;

namespace RESTTest.ViewModels;

/// <summary>One editable row of a multipart body: a text field, or a file to upload.</summary>
public partial class MultipartPartViewModel : EditableViewModel
{
    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private string _key = string.Empty;

    /// <summary>The text to send, or the file's path when <see cref="IsFile"/> is set.</summary>
    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    private bool _isFile;

    public MultipartPartViewModel()
    {
    }

    public MultipartPartViewModel(MultipartPartModel model)
    {
        _enabled = model.Enabled;
        _key = model.Key;
        _value = model.Value;
        _isFile = model.IsFile;
    }

    public MultipartPartModel ToModel() => new()
    {
        Enabled = Enabled,
        Key = Key,
        Value = Value,
        IsFile = IsFile,
    };

    partial void OnIsFileChanged(bool value)
    {
        // A typed sentence is not a path and a path is not a sentence: switching the kind
        // would leave a value that is wrong for the new one, so start the value afresh.
        Value = string.Empty;
    }
}
