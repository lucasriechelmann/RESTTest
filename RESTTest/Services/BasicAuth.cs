using System.Text;

namespace RESTTest.Services;

/// <summary>
/// The "Authorization: Basic ..." value, in one place so the sender, the cURL exporter and the
/// cURL importer agree on how credentials are encoded.
/// </summary>
public static class BasicAuth
{
    private const string Scheme = "Basic";

    /// <summary>Base64 of UTF-8 "user:password", the part after the scheme name.</summary>
    public static string Encode(string username, string password) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));

    /// <summary>The complete header value, "Basic dXNlcjpwYXNz".</summary>
    public static string HeaderValue(string username, string password) =>
        $"{Scheme} {Encode(username, password)}";

    /// <summary>
    /// Reads a header value back into its credentials. False when it is not a Basic header, is not
    /// valid base64, or does not contain a colon (so there is no way to tell user from password).
    /// </summary>
    public static bool TryParse(string headerValue, out string username, out string password)
    {
        username = password = string.Empty;

        var trimmed = headerValue.Trim();
        if (!trimmed.StartsWith(Scheme + " ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(trimmed[(Scheme.Length + 1)..].Trim());
            var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            var colon = text.IndexOf(':');
            if (colon < 0)
            {
                return false;
            }

            username = text[..colon];
            password = text[(colon + 1)..];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
