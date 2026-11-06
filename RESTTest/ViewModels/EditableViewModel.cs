using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RESTTest.ViewModels;

/// <summary>
/// Base class for every ViewModel whose state is persisted. Any property change bubbles a
/// <see cref="Dirty"/> event up the ownership chain, which is what lets <see cref="MainViewModel"/>
/// debounce a single save instead of reflecting over the whole object graph looking for edits.
/// </summary>
public abstract class EditableViewModel : ObservableObject
{
    /// <summary>Raised when this ViewModel, or anything it owns, changes persisted state.</summary>
    public event EventHandler? Dirty;

    /// <summary>
    /// Properties that exist only for the view (selection, inline-rename state) and so must not
    /// cause a save. Without this, clicking around the tree would rewrite the file.
    /// </summary>
    protected virtual bool IsTransient(string? propertyName) => false;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (!IsTransient(e.PropertyName))
        {
            RaiseDirty();
        }
    }

    protected void RaiseDirty() => Dirty?.Invoke(this, EventArgs.Empty);

    protected void Attach(EditableViewModel? child)
    {
        if (child is null)
        {
            return;
        }

        // Unsubscribe first so re-attaching the same child cannot double-fire.
        child.Dirty -= OnChildDirty;
        child.Dirty += OnChildDirty;
    }

    protected void Detach(EditableViewModel? child)
    {
        if (child is not null)
        {
            child.Dirty -= OnChildDirty;
        }
    }

    /// <summary>
    /// Keeps <see cref="Dirty"/> subscriptions in step with a collection's contents, and treats
    /// add/remove/reorder as a change in its own right.
    /// </summary>
    protected void TrackChildren<T>(ObservableCollection<T> collection)
        where T : EditableViewModel
    {
        foreach (var item in collection)
        {
            Attach(item);
        }

        collection.CollectionChanged += (_, args) =>
        {
            if (args.OldItems is not null)
            {
                foreach (var removed in args.OldItems.OfType<T>())
                {
                    Detach(removed);
                }
            }

            if (args.NewItems is not null)
            {
                foreach (var added in args.NewItems.OfType<T>())
                {
                    Attach(added);
                }
            }

            RaiseDirty();
        };
    }

    private void OnChildDirty(object? sender, EventArgs e) => RaiseDirty();
}
