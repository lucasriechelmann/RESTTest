using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RESTTest.Models;

namespace RESTTest.ViewModels;

/// <summary>
/// A collection: the shared list of base URLs plus the folder/request tree, and the owner of
/// every structural tree operation (insert, remove, move) so the views stay logic-free.
/// </summary>
public partial class CollectionViewModel : EditableViewModel
{
    [ObservableProperty]
    private string _name = "New Collection";

    [ObservableProperty]
    private EnvironmentViewModel? _activeEnvironment;

    /// <summary>Kept so the active environment's own URL edits can refresh <see cref="ActiveBaseUrl"/>.</summary>
    private EnvironmentViewModel? _watchedEnvironment;

    public CollectionViewModel()
    {
        Id = Guid.NewGuid().ToString("N");
        Wire();
    }

    private CollectionViewModel(string id)
    {
        Id = id;
    }

    /// <summary>
    /// Hooks change tracking. Called after the initial load in <see cref="FromModel"/> so that
    /// building the tree from disk is not mistaken for an edit that needs saving.
    /// </summary>
    private void Wire()
    {
        TrackChildren(Environments);
        TrackChildren(Items);
        Environments.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasEnvironments));

        // Stored values are immutable rows, so a change to the list is the whole edit.
        CapturedValues.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasCapturedValues));
            RaiseDirty();
        };

        // A capture renamed or added anywhere in the tree changes what the "from capture" pickers offer.
        Dirty += (_, _) => RefreshCaptureNames();
        RefreshCaptureNames();
    }

    /// <summary>Values the captures of this collection's requests stored, by name. Persisted.</summary>
    public ObservableCollection<CapturedValueViewModel> CapturedValues { get; } = [];

    public bool HasCapturedValues => CapturedValues.Count > 0;

    /// <summary>
    /// Every name a "from capture" field can pick: the names of the capture rules in this
    /// collection and of the values already stored, sorted. Replaced only when the set changes, so
    /// an editable picker being typed in does not have its list swapped under it on every key.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<string> _captureNames = [];

    /// <summary>The stored values as a lookup for <see cref="Services.CaptureResolver"/>; names ignore case.</summary>
    public IReadOnlyDictionary<string, string> CaptureValueMap()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in CapturedValues)
        {
            map[value.Name] = value.Value;
        }

        return map;
    }

    /// <summary>
    /// Keeps the values that were captured, replacing any older value of the same name. A failed
    /// capture is not stored, so a bad response never wipes a good token.
    /// </summary>
    public void StoreCaptures(IEnumerable<Services.CaptureResult> results, string sourceRequest)
    {
        foreach (var result in results.Where(r => r.Succeeded))
        {
            var row = new CapturedValueViewModel(new CapturedValueModel
            {
                Name = result.Name,
                Value = result.Value,
                Source = sourceRequest,
                CapturedAt = DateTimeOffset.Now,
            });

            var index = IndexOfCapturedValue(result.Name);
            if (index < 0)
            {
                CapturedValues.Add(row);
            }
            else if (CapturedValues[index].Value != result.Value || CapturedValues[index].Source != sourceRequest)
            {
                CapturedValues[index] = row;
            }
        }
    }

    private int IndexOfCapturedValue(string name)
    {
        for (var i = 0; i < CapturedValues.Count; i++)
        {
            if (string.Equals(CapturedValues[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private void RefreshCaptureNames()
    {
        var names = AllNodes()
            .Where(node => node.Request is not null)
            .SelectMany(node => node.Request!.Captures)
            .Select(capture => capture.Name.Trim())
            .Concat(CapturedValues.Select(value => value.Name))
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!names.SequenceEqual(CaptureNames, StringComparer.Ordinal))
        {
            CaptureNames = names;
        }
    }

    [RelayCommand]
    private void RemoveCapturedValue(CapturedValueViewModel? value)
    {
        if (value is not null)
        {
            CapturedValues.Remove(value);
        }
    }

    [RelayCommand]
    private void ClearCapturedValues() => CapturedValues.Clear();

    public string Id { get; }

    public ObservableCollection<EnvironmentViewModel> Environments { get; } = [];

    public ObservableCollection<NodeViewModel> Items { get; } = [];

    /// <summary>The host every request in this collection is currently pointed at.</summary>
    public string? ActiveBaseUrl => ActiveEnvironment?.BaseUrl;

    public bool HasEnvironments => Environments.Count > 0;

    public static CollectionViewModel FromModel(CollectionModel model)
    {
        var collection = new CollectionViewModel(
            string.IsNullOrWhiteSpace(model.Id) ? Guid.NewGuid().ToString("N") : model.Id)
        {
            Name = model.Name,
        };

        foreach (var environment in model.Environments)
        {
            collection.Environments.Add(new EnvironmentViewModel(environment));
        }

        foreach (var item in model.Items)
        {
            collection.Items.Add(NodeViewModel.FromModel(item));
        }

        foreach (var value in model.CapturedValues ?? [])
        {
            if (!string.IsNullOrWhiteSpace(value.Name))
            {
                collection.CapturedValues.Add(new CapturedValueViewModel(value));
            }
        }

        collection.ActiveEnvironment =
            collection.Environments.FirstOrDefault(e => e.Id == model.ActiveEnvironmentId)
            ?? collection.Environments.FirstOrDefault();

        collection.Wire();

        return collection;
    }

    public CollectionModel ToModel() => new()
    {
        Id = Id,
        Name = Name,
        ActiveEnvironmentId = ActiveEnvironment?.Id,
        Environments = [.. Environments.Select(e => e.ToModel())],
        Items = [.. Items.Select(item => item.ToModel())],
        CapturedValues = [.. CapturedValues.Select(value => value.ToModel())],
    };

    /// <summary>Every node in the tree, depth first.</summary>
    public IEnumerable<NodeViewModel> AllNodes() => Items.SelectMany(item => item.SelfAndDescendants());

    /// <summary>The folder containing <paramref name="node"/>, or null when it sits at the root.</summary>
    public NodeViewModel? FindParent(NodeViewModel node) =>
        AllNodes().FirstOrDefault(candidate => candidate.Children.Contains(node));

    /// <summary>The list <paramref name="node"/> lives in, which is the root list for top-level nodes.</summary>
    public ObservableCollection<NodeViewModel> GetSiblings(NodeViewModel node) =>
        FindParent(node)?.Children ?? Items;

    /// <summary>
    /// Where a new folder or request should go: inside the selection if it is a folder, beside it
    /// if it is a request, otherwise at the root.
    /// </summary>
    public ObservableCollection<NodeViewModel> GetInsertTarget(NodeViewModel? selected)
    {
        if (selected is null)
        {
            return Items;
        }

        if (selected.IsFolder)
        {
            selected.IsExpanded = true;
            return selected.Children;
        }

        return GetSiblings(selected);
    }

    public void Remove(NodeViewModel node) => GetSiblings(node).Remove(node);

    [RelayCommand]
    private void AddEnvironment()
    {
        var environment = new EnvironmentViewModel
        {
            Name = $"URL {Environments.Count + 1}",
            BaseUrl = "https://",
        };

        Environments.Add(environment);
        ActiveEnvironment ??= environment;
    }

    [RelayCommand]
    private void RemoveEnvironment(EnvironmentViewModel? environment)
    {
        if (environment is null)
        {
            return;
        }

        Environments.Remove(environment);

        // Never leave the collection pointing at an environment that no longer exists.
        if (ReferenceEquals(ActiveEnvironment, environment))
        {
            ActiveEnvironment = Environments.FirstOrDefault();
        }
    }

    /// <summary>
    /// Guards the two illegal drops: onto itself, and into its own subtree (which would detach
    /// that whole branch from the tree).
    /// </summary>
    public bool CanMove(NodeViewModel source, NodeViewModel? target, DropPlacement placement)
    {
        if (target is null)
        {
            return true;
        }

        if (ReferenceEquals(source, target))
        {
            return false;
        }

        if (placement == DropPlacement.Inside && !target.IsFolder)
        {
            return false;
        }

        return !source.SelfAndDescendants().Any(node => ReferenceEquals(node, target));
    }

    /// <summary>
    /// Reparents or reorders <paramref name="source"/>. A null target means "the end of the root
    /// list", which is what dropping on empty sidebar space does.
    /// </summary>
    public void Move(NodeViewModel source, NodeViewModel? target, DropPlacement placement)
    {
        if (!CanMove(source, target, placement))
        {
            return;
        }

        GetSiblings(source).Remove(source);

        if (target is null)
        {
            Items.Add(source);
            return;
        }

        if (placement == DropPlacement.Inside)
        {
            target.Children.Add(source);
            target.IsExpanded = true;
            return;
        }

        // Resolved after the removal above, so the index already accounts for the gap when the
        // node is moving within its own list.
        var siblings = GetSiblings(target);
        var index = siblings.IndexOf(target);
        if (index < 0)
        {
            Items.Add(source);
            return;
        }

        siblings.Insert(placement == DropPlacement.After ? index + 1 : index, source);
    }

    protected override bool IsTransient(string? propertyName) =>
        propertyName is nameof(CaptureNames) or nameof(HasCapturedValues);

    partial void OnActiveEnvironmentChanged(EnvironmentViewModel? value)
    {
        // Re-point the watcher: editing the selected environment's URL must update the
        // resolved-URL preview immediately, not only when the selection changes.
        if (_watchedEnvironment is not null)
        {
            _watchedEnvironment.PropertyChanged -= OnActiveEnvironmentPropertyChanged;
        }

        _watchedEnvironment = value;

        if (_watchedEnvironment is not null)
        {
            _watchedEnvironment.PropertyChanged += OnActiveEnvironmentPropertyChanged;
        }

        OnPropertyChanged(nameof(ActiveBaseUrl));
    }

    private void OnActiveEnvironmentPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EnvironmentViewModel.BaseUrl))
        {
            OnPropertyChanged(nameof(ActiveBaseUrl));
        }
    }
}
