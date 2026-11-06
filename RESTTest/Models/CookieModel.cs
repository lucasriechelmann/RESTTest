namespace RESTTest.Models;

/// <summary>Persisted form of one cookie held in the cookie jar.</summary>
public sealed class CookieModel
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    /// <summary>A leading dot ("." + host) means the cookie also applies to subdomains; none means that host only.</summary>
    public string Domain { get; set; } = string.Empty;

    public string Path { get; set; } = "/";

    /// <summary>UTC expiry, or null for a session cookie (no expiry sent by the server).</summary>
    public DateTime? Expires { get; set; }

    public bool Secure { get; set; }

    public bool HttpOnly { get; set; }
}
