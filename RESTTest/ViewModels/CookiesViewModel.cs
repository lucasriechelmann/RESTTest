using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RESTTest.Services;

namespace RESTTest.ViewModels;

/// <summary>
/// Backs the Cookies dialog: the on/off switch for the jar and the list of cookies in it. Not
/// persisted itself; the jar raises <see cref="CookieJar.Changed"/> and the owner saves it.
/// </summary>
public sealed partial class CookiesViewModel : ObservableObject
{
    private readonly CookieJar _jar;
    private readonly IDialogService _dialogs;

    public CookiesViewModel(CookieJar jar, IDialogService dialogs)
    {
        _jar = jar;
        _dialogs = dialogs;
        Refresh();
    }

    public ObservableCollection<CookieEntryViewModel> Cookies { get; } = [];

    public bool HasCookies => Cookies.Count > 0;

    /// <summary>Whether cookies are stored and sent. Off means requests go out exactly as typed.</summary>
    public bool Enabled
    {
        get => _jar.Enabled;
        set
        {
            if (_jar.Enabled == value)
            {
                return;
            }

            _jar.Enabled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StateNote));
        }
    }

    /// <summary>Said under the switch so the effect of each position is clear.</summary>
    public string StateNote => Enabled
        ? "Cookies a server sets are kept and sent back on later requests to that host. A request with its own Cookie header skips the jar."
        : "Cookies are off: none are stored or sent, and requests go out exactly as typed.";

    /// <summary>Re-reads the jar, for when it may have changed since the list was built.</summary>
    public void Refresh()
    {
        Cookies.Clear();

        foreach (var cookie in _jar.Snapshot())
        {
            Cookies.Add(new CookieEntryViewModel(cookie));
        }

        OnPropertyChanged(nameof(HasCookies));
    }

    [RelayCommand]
    private void Delete(CookieEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        _jar.Remove(entry.Model);
        Refresh();
    }

    [RelayCommand]
    private void ClearAll()
    {
        if (!HasCookies)
        {
            return;
        }

        if (_dialogs.Confirm("Clear cookies", $"Delete all {Cookies.Count} cookies? You will have to sign in again where a request relied on one.", "Clear"))
        {
            _jar.Clear();
            Refresh();
        }
    }
}
