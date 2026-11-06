using CommunityToolkit.Mvvm.ComponentModel;
using RESTTest.Models;

namespace RESTTest.ViewModels;

/// <summary>
/// A named base URL on the collection. The whole point of the design: requests store only a
/// method and a path, and switching the selected environment re-targets all of them at once.
/// </summary>
public partial class EnvironmentViewModel : EditableViewModel
{
    [ObservableProperty]
    private string _name = "New URL";

    [ObservableProperty]
    private string _baseUrl = string.Empty;

    public EnvironmentViewModel()
    {
        Id = Guid.NewGuid().ToString("N");
    }

    public EnvironmentViewModel(EnvironmentModel model)
    {
        Id = model.Id;
        _name = model.Name;
        _baseUrl = model.BaseUrl;
    }

    public string Id { get; }

    /// <summary>Label for the environment combo box, showing both the name and the target.</summary>
    public string Display => string.IsNullOrWhiteSpace(BaseUrl)
        ? Name
        : $"{Name}  •  {BaseUrl}";

    public EnvironmentModel ToModel() => new()
    {
        Id = Id,
        Name = Name,
        BaseUrl = BaseUrl,
    };

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(Display));

    partial void OnBaseUrlChanged(string value) => OnPropertyChanged(nameof(Display));
}
