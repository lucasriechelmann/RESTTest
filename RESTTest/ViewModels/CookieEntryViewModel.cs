using RESTTest.Models;

namespace RESTTest.ViewModels;

/// <summary>One row of the Cookies dialog. Read-only: a cookie is changed by the server or deleted here.</summary>
public sealed class CookieEntryViewModel(CookieModel model)
{
    public CookieModel Model { get; } = model;

    /// <summary>The host without the leading dot that marks a subdomain-wide cookie.</summary>
    public string Domain => Model.Domain.TrimStart('.');

    public string Name => Model.Name;

    public string Value => Model.Value;

    public string Path => Model.Path;

    public string ExpiresText => Model.Expires is { } at
        ? at.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
        : "Session";

    /// <summary>"Secure, HttpOnly, subdomains": the flags that change when the cookie is sent or read.</summary>
    public string FlagsText
    {
        get
        {
            var flags = new List<string>();
            if (Model.Secure)
            {
                flags.Add("Secure");
            }

            if (Model.HttpOnly)
            {
                flags.Add("HttpOnly");
            }

            if (Model.Domain.StartsWith('.'))
            {
                flags.Add("subdomains");
            }

            return string.Join(", ", flags);
        }
    }
}
