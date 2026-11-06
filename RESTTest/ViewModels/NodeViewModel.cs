using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RESTTest.Models;

namespace RESTTest.ViewModels;

/// <summary>
/// One entry in the sidebar tree. Folders and requests share this single type so that renaming,
/// reordering and drag-and-drop are one uniform operation rather than four special cases.
/// </summary>
public partial class NodeViewModel : EditableViewModel
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>View state: which row is highlighted. Never persisted.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>View state: swaps the row's label for an inline edit box.</summary>
    [ObservableProperty]
    private bool _isRenaming;

    /// <summary>Scratch text for the inline editor, so Escape can discard it.</summary>
    [ObservableProperty]
    private string _renameText = string.Empty;

    private NodeViewModel(string id, NodeKind kind, string name, RequestViewModel? request)
    {
        Id = id;
        Kind = kind;
        _name = name;
        Request = request;

        if (Request is not null)
        {
            Attach(Request);
            // The sidebar shows the method as a coloured badge, so keep it in step.
            Request.PropertyChanged += OnRequestPropertyChanged;
        }

        TrackChildren(Children);
    }

    public string Id { get; }

    public NodeKind Kind { get; }

    /// <summary>Null for folders.</summary>
    public RequestViewModel? Request { get; }

    public ObservableCollection<NodeViewModel> Children { get; } = [];

    public bool IsFolder => Kind == NodeKind.Folder;

    public bool IsRequest => Kind == NodeKind.Request;

    /// <summary>Badge text in the sidebar; abbreviated so long method names still fit.</summary>
    public string MethodLabel => Request?.Method switch
    {
        null => string.Empty,
        "DELETE" => "DEL",
        "OPTIONS" => "OPT",
        "PATCH" => "PTCH",
        var method => method,
    };

    public static NodeViewModel CreateFolder(string name = "New Folder") =>
        new(Guid.NewGuid().ToString("N"), NodeKind.Folder, name, request: null);

    public static NodeViewModel CreateRequest(string name = "New Request") =>
        new(Guid.NewGuid().ToString("N"), NodeKind.Request, name, new RequestViewModel());

    public static NodeViewModel FromModel(NodeModel model)
    {
        var node = new NodeViewModel(
            string.IsNullOrWhiteSpace(model.Id) ? Guid.NewGuid().ToString("N") : model.Id,
            model.Kind,
            model.Name,
            model.Kind == NodeKind.Request ? new RequestViewModel(model.Request ?? new RequestModel()) : null)
        {
            IsExpanded = model.IsExpanded,
        };

        foreach (var child in model.Children)
        {
            node.Children.Add(FromModel(child));
        }

        return node;
    }

    public NodeModel ToModel() => new()
    {
        Id = Id,
        Kind = Kind,
        Name = Name,
        IsExpanded = IsExpanded,
        Request = Request?.ToModel(),
        Children = [.. Children.Select(child => child.ToModel())],
    };

    /// <summary>Deep copy with fresh ids, for the Duplicate command.</summary>
    public NodeViewModel Clone(string newName)
    {
        var model = ToModel();
        model.Name = newName;
        ReassignIds(model);
        return FromModel(model);
    }

    private static void ReassignIds(NodeModel model)
    {
        model.Id = Guid.NewGuid().ToString("N");
        foreach (var child in model.Children)
        {
            ReassignIds(child);
        }
    }

    /// <summary>Depth-first walk including this node, used for lookups and ancestor checks.</summary>
    public IEnumerable<NodeViewModel> SelfAndDescendants()
    {
        yield return this;

        foreach (var child in Children)
        {
            foreach (var descendant in child.SelfAndDescendants())
            {
                yield return descendant;
            }
        }
    }

    [RelayCommand]
    private void BeginRename()
    {
        RenameText = Name;
        IsRenaming = true;
    }

    [RelayCommand]
    private void CommitRename()
    {
        if (!IsRenaming)
        {
            return;
        }

        var trimmed = RenameText.Trim();
        if (trimmed.Length > 0)
        {
            Name = trimmed;
        }

        IsRenaming = false;
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;

    protected override bool IsTransient(string? propertyName) =>
        propertyName is nameof(IsSelected) or nameof(IsRenaming) or nameof(RenameText);

    private void OnRequestPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RequestViewModel.Method))
        {
            OnPropertyChanged(nameof(MethodLabel));
        }
    }
}
