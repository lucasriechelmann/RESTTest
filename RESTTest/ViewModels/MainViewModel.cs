using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RESTTest.Models;
using RESTTest.Services;

namespace RESTTest.ViewModels;

/// <summary>
/// Root ViewModel: owns the collection list, the current selection, the response pane, and the
/// debounced auto-save that keeps the JSON files in step with the UI.
/// </summary>
public partial class MainViewModel : ObservableObject, INodeDropHandler
{
    private readonly ICollectionStore _store;
    private readonly IHttpRequestSender _sender;
    private readonly IDialogService _dialogs;
    private readonly IHistoryStore _historyStore;

    /// <summary>Set when the history changed since the last write; flushed by <see cref="_saveTimer"/>.</summary>
    private bool _historyDirty;

    /// <summary>
    /// True once the user pressed Cancel for the send in flight. Such a send is not history: the
    /// user abandoned it. A timeout cancels the same token, so the token alone cannot tell them apart.
    /// </summary>
    private bool _cancelledByUser;

    /// <summary>Collections edited since the last write. Flushed by <see cref="_saveTimer"/>.</summary>
    private readonly HashSet<CollectionViewModel> _pendingSaves = [];

    private readonly DispatcherTimer _saveTimer;

    private CancellationTokenSource? _sendCancellation;
    private bool _isLoading;

    [ObservableProperty]
    private CollectionViewModel? _selectedCollection;

    [ObservableProperty]
    private NodeViewModel? _selectedNode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isSending;

    /// <summary>The absolute URL the current request would hit, shown under the path box.</summary>
    [ObservableProperty]
    private string _resolvedUrl = string.Empty;

    [ObservableProperty]
    private string? _statusMessage;

    public MainViewModel(
        ICollectionStore store,
        IHttpRequestSender sender,
        IDialogService dialogs,
        IHistoryStore historyStore)
    {
        _store = store;
        _sender = sender;
        _dialogs = dialogs;
        _historyStore = historyStore;

        History = new HistoryViewModel(dialogs, historyStore.Load());
        History.Changed += (_, _) => QueueHistorySave();
        History.SaveAsRequestHandler = SaveHistoryEntryAsRequest;

        // Coalesces a burst of keystrokes into one write.
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += OnSaveTimerTick;

        Load();
    }

    public ObservableCollection<CollectionViewModel> Collections { get; } = [];

    public ResponseViewModel Response { get; } = new();

    public HistoryViewModel History { get; }

    /// <summary>The request being edited, or null when a folder (or nothing) is selected.</summary>
    public RequestViewModel? ActiveRequest => SelectedNode?.Request;

    public bool HasActiveRequest => ActiveRequest is not null;

    public bool HasCollections => Collections.Count > 0;

    public string WorkspacePath => _store.WorkspacePath;

    /// <summary>Instance properties rather than static, so plain {Binding} reaches them.</summary>
    public IReadOnlyList<string> Methods => RequestViewModel.Methods;

    public IReadOnlyList<AuthKind> AuthKinds => RequestViewModel.AuthKinds;

    #region Loading and saving

    private void Load()
    {
        _isLoading = true;

        try
        {
            var models = _store.LoadAll();

            if (models.Count == 0)
            {
                // First run: ship something runnable rather than an empty window.
                var sample = CreateSampleCollection();
                Collections.Add(sample);
                QueueSave(sample);
            }
            else
            {
                foreach (var model in models)
                {
                    Collections.Add(CollectionViewModel.FromModel(model));
                }
            }

            foreach (var collection in Collections)
            {
                collection.Dirty += OnCollectionDirty;
            }

            var settings = _store.LoadSettings();

            SelectedCollection =
                Collections.FirstOrDefault(c => c.Id == settings.LastCollectionId)
                ?? Collections.FirstOrDefault();

            if (settings.LastSelectedNodeId is { } nodeId && SelectedCollection is not null)
            {
                SelectedNode = SelectedCollection.AllNodes().FirstOrDefault(n => n.Id == nodeId);
            }

            SelectedNode ??= SelectedCollection?.AllNodes().FirstOrDefault(n => n.IsRequest);
        }
        finally
        {
            _isLoading = false;
        }

        OnPropertyChanged(nameof(HasCollections));
    }

    private void OnCollectionDirty(object? sender, EventArgs e)
    {
        if (_isLoading || sender is not CollectionViewModel collection)
        {
            return;
        }

        QueueSave(collection);
        RefreshResolvedUrl();
    }

    private void QueueSave(CollectionViewModel collection)
    {
        _pendingSaves.Add(collection);

        // Restarting the timer is what turns a burst of edits into a single write.
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnSaveTimerTick(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        FlushPendingSaves();
    }

    private void QueueHistorySave()
    {
        _historyDirty = true;

        // Same debounce as the collections: one write for a burst of changes.
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes anything still queued. Called by the timer and on window close.</summary>
    public void FlushPendingSaves()
    {
        if (_historyDirty)
        {
            _historyDirty = false;

            try
            {
                _historyStore.Save(History.ToModels());
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not save the history: {ex.Message}";
            }
        }

        if (_pendingSaves.Count == 0)
        {
            return;
        }

        var collections = _pendingSaves.ToArray();
        _pendingSaves.Clear();

        foreach (var collection in collections)
        {
            try
            {
                _store.Save(collection.ToModel());
            }
            catch (Exception ex)
            {
                StatusMessage = $"Could not save '{collection.Name}': {ex.Message}";
            }
        }
    }

    /// <summary>Persists which collection and request were open, for the next launch.</summary>
    public void SaveUiState()
    {
        _store.SaveSettings(new AppSettings
        {
            LastCollectionId = SelectedCollection?.Id,
            LastSelectedNodeId = SelectedNode?.Id,
        });
    }

    private static CollectionViewModel CreateSampleCollection()
    {
        var collection = new CollectionViewModel { Name = "Sample API" };

        collection.Environments.Add(new EnvironmentViewModel
        {
            Name = "Local",
            BaseUrl = "http://localhost:5000",
        });

        var demo = new EnvironmentViewModel
        {
            Name = "JSONPlaceholder",
            BaseUrl = "https://jsonplaceholder.typicode.com",
        };
        collection.Environments.Add(demo);
        collection.ActiveEnvironment = demo;

        var folder = NodeViewModel.CreateFolder("Posts");
        folder.IsExpanded = true;

        var list = NodeViewModel.CreateRequest("List posts");
        list.Request!.Method = "GET";
        list.Request.Path = "/posts";
        list.Request.QueryParams.Add(new KeyValueViewModel { Enabled = false, Key = "userId", Value = "1" });

        var create = NodeViewModel.CreateRequest("Create post");
        create.Request!.Method = "POST";
        create.Request.Path = "/posts";
        create.Request.Body = "{\n  \"title\": \"Hello\",\n  \"body\": \"From RESTTest\",\n  \"userId\": 1\n}";

        folder.Children.Add(list);
        folder.Children.Add(create);
        collection.Items.Add(folder);

        return collection;
    }

    #endregion

    #region Collection commands

    [RelayCommand]
    private void NewCollection()
    {
        var name = _dialogs.Prompt("New collection", "Collection name", "New Collection");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var collection = new CollectionViewModel { Name = name.Trim() };
        collection.Environments.Add(new EnvironmentViewModel { Name = "Local", BaseUrl = "http://localhost:5000" });
        collection.ActiveEnvironment = collection.Environments[0];
        collection.Dirty += OnCollectionDirty;

        Collections.Add(collection);
        SelectedCollection = collection;
        OnPropertyChanged(nameof(HasCollections));

        QueueSave(collection);
    }

    [RelayCommand]
    private void RenameCollection()
    {
        if (SelectedCollection is not { } collection)
        {
            return;
        }

        var name = _dialogs.Prompt("Rename collection", "Collection name", collection.Name);
        if (!string.IsNullOrWhiteSpace(name))
        {
            collection.Name = name.Trim();
        }
    }

    [RelayCommand]
    private void DeleteCollection()
    {
        if (SelectedCollection is not { } collection)
        {
            return;
        }

        if (!_dialogs.Confirm(
                "Delete collection",
                $"Delete '{collection.Name}' and everything in it? This cannot be undone.",
                "Delete"))
        {
            return;
        }

        collection.Dirty -= OnCollectionDirty;
        _pendingSaves.Remove(collection);
        Collections.Remove(collection);

        try
        {
            _store.Delete(collection.Id);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not delete the file: {ex.Message}";
        }

        SelectedCollection = Collections.FirstOrDefault();
        OnPropertyChanged(nameof(HasCollections));
    }

    [RelayCommand]
    private void ManageEnvironments()
    {
        if (SelectedCollection is { } collection)
        {
            _dialogs.ShowEnvironments(collection);
        }
    }

    #endregion

    #region Tree commands

    [RelayCommand]
    private void NewFolder()
    {
        if (SelectedCollection is not { } collection)
        {
            return;
        }

        var folder = NodeViewModel.CreateFolder();
        collection.GetInsertTarget(SelectedNode).Add(folder);

        SelectedNode = folder;
        folder.BeginRenameCommand.Execute(null);
    }

    [RelayCommand]
    private void NewRequest()
    {
        if (SelectedCollection is not { } collection)
        {
            return;
        }

        var request = NodeViewModel.CreateRequest();
        collection.GetInsertTarget(SelectedNode).Add(request);

        SelectedNode = request;
        request.BeginRenameCommand.Execute(null);
    }

    [RelayCommand]
    private void RenameSelected() => SelectedNode?.BeginRenameCommand.Execute(null);

    [RelayCommand]
    private void DuplicateSelected()
    {
        if (SelectedCollection is not { } collection || SelectedNode is not { } node)
        {
            return;
        }

        var copy = node.Clone($"{node.Name} copy");
        var siblings = collection.GetSiblings(node);
        siblings.Insert(siblings.IndexOf(node) + 1, copy);

        SelectedNode = copy;
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedCollection is not { } collection || SelectedNode is not { } node)
        {
            return;
        }

        var what = node.IsFolder ? "folder" : "request";

        // Everything below the node, not just its direct children, since the whole branch goes.
        var contained = node.SelfAndDescendants().Count() - 1;
        var extra = node.IsFolder && contained > 0
            ? $" and the {contained} item{(contained == 1 ? string.Empty : "s")} inside it"
            : string.Empty;

        if (!_dialogs.Confirm("Delete", $"Delete the {what} '{node.Name}'{extra}?", "Delete"))
        {
            return;
        }

        collection.Remove(node);
        SelectedNode = collection.AllNodes().FirstOrDefault(n => n.IsRequest);
    }

    /// <summary>Entry point for the drag-and-drop behavior; all tree surgery lives in the ViewModel.</summary>
    public void MoveNode(NodeViewModel source, NodeViewModel? target, DropPlacement placement)
    {
        if (SelectedCollection is not { } collection)
        {
            return;
        }

        collection.Move(source, target, placement);
        SelectedNode = source;
    }

    public bool CanMoveNode(NodeViewModel source, NodeViewModel? target, DropPlacement placement) =>
        SelectedCollection?.CanMove(source, target, placement) ?? false;

    #endregion

    #region Sending

    private bool CanSend => ActiveRequest is not null && !IsSending;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        if (SelectedCollection is not { } collection || ActiveRequest is not { } request)
        {
            return;
        }

        IsSending = true;
        _cancelledByUser = false;
        StatusMessage = null;

        _sendCancellation?.Dispose();
        _sendCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        try
        {
            var model = request.ToModel();
            var result = await _sender
                .SendAsync(collection.ActiveBaseUrl, model, _sendCancellation.Token)
                .ConfigureAwait(true);

            var tests = AssertionEvaluator.EvaluateAll(model.Assertions, result);
            Response.Load(result, tests);

            if (!_cancelledByUser)
            {
                History.Record(collection.Name, model, result, tests);
            }
        }
        finally
        {
            IsSending = false;
        }
    }

    [RelayCommand]
    private void CancelSend()
    {
        _cancelledByUser = true;
        _sendCancellation?.Cancel();
    }

    [RelayCommand]
    private void ShowHistory() => _dialogs.ShowHistory(History);

    /// <summary>
    /// Copies a history entry's request into the open collection, next to the selection. Returns
    /// the message the history dialog shows.
    /// </summary>
    private string? SaveHistoryEntryAsRequest(HistoryEntryViewModel entry)
    {
        if (SelectedCollection is not { } collection)
        {
            return "Create or select a collection first.";
        }

        var path = entry.Path.Trim();
        var name = path.Length == 0 ? entry.Method : $"{entry.Method} {path}";

        // A blank id makes FromModel mint a fresh one, so this never collides with the original.
        var node = NodeViewModel.FromModel(new NodeModel
        {
            Kind = NodeKind.Request,
            Name = name,
            Request = entry.Request,
        });

        collection.GetInsertTarget(SelectedNode).Add(node);
        SelectedNode = node;

        return $"Saved \"{name}\" to {collection.Name}.";
    }

    /// <summary>
    /// Runs every request in the selected folder, or in the whole collection when a request (or
    /// nothing) is selected, against the collection's active base URL.
    /// </summary>
    [RelayCommand]
    private void RunSelected()
    {
        if (SelectedCollection is not { } collection)
        {
            return;
        }

        var folder = SelectedNode is { IsFolder: true } selectedFolder ? selectedFolder : null;
        var nodes = folder is null ? collection.AllNodes() : folder.SelfAndDescendants();
        var requests = nodes.Where(node => node.IsRequest).ToList();

        if (requests.Count == 0)
        {
            StatusMessage = "There are no requests to run.";
            return;
        }

        StatusMessage = null;

        var title = folder is null ? collection.Name : $"{collection.Name} / {folder.Name}";
        var items = requests.Select(node => new RunItemViewModel(node.Name, node.Request!.ToModel()));
        var runner = new RunnerViewModel(_sender, collection.ActiveBaseUrl, title, items);

        // Start first, then show: the dialog blocks, and the run's continuations are delivered by
        // the dispatcher loop that ShowDialog keeps pumping.
        _ = runner.RunAsync();
        _dialogs.ShowRunner(runner);
    }

    [RelayCommand]
    private void CopyResolvedUrl()
    {
        if (!string.IsNullOrWhiteSpace(ResolvedUrl))
        {
            _dialogs.CopyToClipboard(ResolvedUrl);
            StatusMessage = "URL copied.";
        }
    }

    [RelayCommand]
    private void CopyResponseBody()
    {
        if (Response.HasResponse)
        {
            _dialogs.CopyToClipboard(Response.PrettyBody);
            StatusMessage = "Response copied.";
        }
    }

    private void RefreshResolvedUrl()
    {
        ResolvedUrl = ActiveRequest is { } request
            ? _sender.BuildUrl(SelectedCollection?.ActiveBaseUrl, request.ToModel())
            : string.Empty;
    }

    #endregion

    #region Selection plumbing

    partial void OnSelectedCollectionChanged(CollectionViewModel? oldValue, CollectionViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnSelectedCollectionPropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnSelectedCollectionPropertyChanged;
        }

        // The old selection belongs to a tree that is no longer shown.
        SelectedNode = newValue?.AllNodes().FirstOrDefault(node => node.IsRequest);
        RefreshResolvedUrl();
    }

    private void OnSelectedCollectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CollectionViewModel.ActiveBaseUrl)
            or nameof(CollectionViewModel.ActiveEnvironment))
        {
            // Switching environment re-targets every request, so the preview must follow.
            RefreshResolvedUrl();
        }
    }

    partial void OnSelectedNodeChanged(NodeViewModel? oldValue, NodeViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
        }

        Response.Clear();
        OnPropertyChanged(nameof(ActiveRequest));
        OnPropertyChanged(nameof(HasActiveRequest));
        SendCommand.NotifyCanExecuteChanged();
        RefreshResolvedUrl();
    }

    #endregion
}
