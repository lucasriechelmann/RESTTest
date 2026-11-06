using System.Net;
using RESTTest.Models;

namespace RESTTest.Services;

/// <summary>
/// The cookies RESTTest keeps between requests, like a browser's: a login response sets a session
/// cookie and every later request to that host sends it back. One jar for the whole app (cookies are
/// keyed by host, as in a browser), shown and edited through the Cookies dialog.
/// </summary>
/// <remarks>
/// Backed by a <see cref="CookieContainer"/> that the HTTP handler uses directly, so cookies set on a
/// redirect response (the usual shape of a login) are kept too. A request that carries its own
/// <c>Cookie</c> header bypasses the jar entirely: what the user typed is what is sent.
/// </remarks>
public sealed class CookieJar
{
    private bool _enabled = true;
    private string _lastSignature = string.Empty;

    /// <summary>The container the HTTP handler reads and writes.</summary>
    public CookieContainer Container { get; } = new();

    /// <summary>When false the jar is neither read nor written. Changing it raises <see cref="Changed"/>.</summary>
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Raised when what should be saved changed: a cookie was added, changed or removed, or the switch flipped.</summary>
    public event EventHandler? Changed;

    /// <summary>Live cookies (expired ones are dropped by the container), ordered by domain, path and name.</summary>
    public IReadOnlyList<CookieModel> Snapshot() =>
        [.. Container.GetAllCookies()
            .Cast<Cookie>()
            .Where(c => !c.Expired)
            .Select(ToModel)
            .OrderBy(c => c.Domain.TrimStart('.'), StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Path, StringComparer.Ordinal)
            .ThenBy(c => c.Name, StringComparer.Ordinal)];

    public CookieJarModel ToModel() => new() { Enabled = Enabled, Cookies = [.. Snapshot()] };

    /// <summary>
    /// Fills the jar from a saved file. Does not raise <see cref="Changed"/>: reading the file back
    /// is not an edit. Entries that are expired or malformed are skipped.
    /// </summary>
    public void Load(CookieJarModel model)
    {
        _enabled = model.Enabled;

        foreach (var saved in model.Cookies)
        {
            if (saved.Expires is { } expires && expires <= DateTime.UtcNow)
            {
                continue;
            }

            try
            {
                var cookie = new Cookie(saved.Name, saved.Value, string.IsNullOrEmpty(saved.Path) ? "/" : saved.Path, saved.Domain)
                {
                    Secure = saved.Secure,
                    HttpOnly = saved.HttpOnly,
                };

                if (saved.Expires is { } at)
                {
                    cookie.Expires = DateTime.SpecifyKind(at, DateTimeKind.Utc).ToLocalTime();
                }

                Container.Add(cookie);
            }
            catch (CookieException)
            {
                // One bad entry must not cost the user the rest of the jar.
            }
            catch (ArgumentException)
            {
            }
        }

        _lastSignature = Signature();
    }

    /// <summary>The value of the <c>Cookie</c> header the jar would send to this URL, or null when there is none.</summary>
    public string? HeaderFor(Uri url)
    {
        var header = Container.GetCookieHeader(url);
        return string.IsNullOrEmpty(header) ? null : header;
    }

    /// <summary>
    /// Called after a request that used the jar. Raises <see cref="Changed"/> only if the cookies
    /// really differ from the last time, so an ordinary request does not rewrite the file.
    /// </summary>
    public void NotifyMaybeChanged()
    {
        var signature = Signature();
        if (signature == _lastSignature)
        {
            return;
        }

        _lastSignature = signature;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(CookieModel cookie)
    {
        var match = Container.GetAllCookies()
            .Cast<Cookie>()
            .FirstOrDefault(c => c.Name == cookie.Name && c.Domain == cookie.Domain && c.Path == cookie.Path);

        if (match is null)
        {
            return;
        }

        // Adding an already-expired cookie is how a CookieContainer deletes one.
        match.Expired = true;
        Container.Add(match);

        _lastSignature = Signature();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        foreach (var cookie in Container.GetAllCookies().Cast<Cookie>())
        {
            cookie.Expired = true;
            Container.Add(cookie);
        }

        _lastSignature = Signature();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string Signature() =>
        string.Join('\n', Snapshot().Select(c => $"{c.Domain}|{c.Path}|{c.Name}|{c.Value}|{c.Expires:O}|{c.Secure}"));

    private static CookieModel ToModel(Cookie cookie) => new()
    {
        Name = cookie.Name,
        Value = cookie.Value,
        Domain = cookie.Domain,
        Path = string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path,
        Expires = cookie.Expires == DateTime.MinValue ? null : cookie.Expires.ToUniversalTime(),
        Secure = cookie.Secure,
        HttpOnly = cookie.HttpOnly,
    };
}
