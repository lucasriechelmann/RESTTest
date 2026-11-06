namespace RESTTest.Models;

/// <summary>The contents of <c>cookies.json</c>: the on/off switch and every cookie in the jar.</summary>
public sealed class CookieJarModel
{
    /// <summary>When false, no cookie is stored or sent, so requests go out exactly as typed.</summary>
    public bool Enabled { get; set; } = true;

    public List<CookieModel> Cookies { get; set; } = [];
}
