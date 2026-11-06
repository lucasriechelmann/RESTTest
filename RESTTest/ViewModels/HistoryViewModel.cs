using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RESTTest.Models;
using RESTTest.Services;

namespace RESTTest.ViewModels;

/// <summary>
/// The list of requests that were sent, newest first. It does no disk access itself: it raises
/// <see cref="Changed"/> and <c>MainViewModel</c> writes the file through the same debounce that
/// saves collections.
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    /// <summary>Oldest entries beyond this are dropped, which bounds the size of the history file.</summary>
    public const int MaxEntries = 100;

    /// <summary>Longest response body kept per entry. Together with the cap above: at most ~10 MB.</summary>
    public const int MaxBodyChars = 100_000;

    private readonly IDialogService _dialogs;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAsRequestCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedCommand))]
    private HistoryEntryViewModel? _selected;

    /// <summary>Feedback from the last "Save as request", shown in the dialog.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    public HistoryViewModel(IDialogService dialogs, IEnumerable<HistoryEntryModel> saved)
    {
        _dialogs = dialogs;

        foreach (var model in saved.OrderByDescending(entry => entry.Timestamp).Take(MaxEntries))
        {
            Entries.Add(new HistoryEntryViewModel(model));
        }

        Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasEntries));
    }

    public ObservableCollection<HistoryEntryViewModel> Entries { get; } = [];

    public bool HasEntries => Entries.Count > 0;

    public bool HasSelection => Selected is not null;

    /// <summary>Raised whenever the list changes, so the owner can queue a save.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Supplied by <c>MainViewModel</c>, which owns the collections. Returns the message to show.
    /// A callback rather than a reference keeps this ViewModel unaware of collections.
    /// </summary>
    public Func<HistoryEntryViewModel, string?>? SaveAsRequestHandler { get; set; }

    /// <summary>Adds the outcome of a send at the top. A transport failure is recorded too.</summary>
    public void Record(
        string collectionName,
        RequestModel request,
        HttpExchangeResult result,
        IReadOnlyList<AssertionResult> tests,
        DateTimeOffset? timestamp = null)
    {
        var truncated = result.Body.Length > MaxBodyChars;

        var model = new HistoryEntryModel
        {
            Timestamp = timestamp ?? DateTimeOffset.Now,
            CollectionName = collectionName,
            Url = result.Url,
            Request = request,
            Completed = result.Completed,
            StatusCode = result.StatusCode,
            ReasonPhrase = result.ReasonPhrase,
            ElapsedMilliseconds = result.ElapsedMilliseconds,
            ContentLength = result.ContentLength,
            ResponseHeaders = [.. result.ResponseHeaders],
            Body = truncated ? result.Body[..MaxBodyChars] : result.Body,
            BodyTruncated = truncated,
            Error = result.Error,
            TestsPassed = tests.Count(t => t.Passed),
            TestsTotal = tests.Count,
        };

        Entries.Insert(0, new HistoryEntryViewModel(model));

        while (Entries.Count > MaxEntries)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<HistoryEntryModel> ToModels() => [.. Entries.Select(entry => entry.Model)];

    private bool HasSelectedEntry() => Selected is not null;

    [RelayCommand(CanExecute = nameof(HasSelectedEntry))]
    private void SaveAsRequest()
    {
        if (Selected is { } entry)
        {
            StatusMessage = SaveAsRequestHandler?.Invoke(entry);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedEntry))]
    private void RemoveSelected()
    {
        if (Selected is not { } entry)
        {
            return;
        }

        var index = Entries.IndexOf(entry);
        Entries.Remove(entry);

        // Keep a neighbour selected so repeated deletes walk down the list.
        Selected = Entries.Count == 0 ? null : Entries[Math.Min(index, Entries.Count - 1)];
        Changed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearAll()
    {
        if (Entries.Count == 0)
        {
            return;
        }

        var what = Entries.Count == 1 ? "the only entry" : $"all {Entries.Count} entries";

        if (!_dialogs.Confirm("Clear history", $"Remove {what} from the history? This cannot be undone.", "Clear"))
        {
            return;
        }

        Selected = null;
        Entries.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedChanged(HistoryEntryViewModel? value)
    {
        StatusMessage = null;
        OnPropertyChanged(nameof(HasSelection));
    }
}
