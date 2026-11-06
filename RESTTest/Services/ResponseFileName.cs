using System.IO;
using System.Text.RegularExpressions;

namespace RESTTest.Services;

/// <summary>Proposes a file name for "Save response": what the server asked for, else the URL, else a typed default.</summary>
public static partial class ResponseFileName
{
    [GeneratedRegex(@"filename\*\s*=\s*(?:UTF-8|utf-8)?'[^']*'(?<name>[^;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Extended();

    [GeneratedRegex("filename\\s*=\\s*(?:\"(?<name>[^\"]*)\"|(?<name>[^;]+))", RegexOptions.IgnoreCase)]
    private static partial Regex Plain();

    public static string Suggest(string url, string? contentType, string? contentDisposition, bool isBinary)
    {
        var named = FromDisposition(contentDisposition);
        if (named.Length > 0)
        {
            return named;
        }

        var segment = LastSegment(url);
        var extension = MimeTypes.ExtensionFor(contentType) ?? (isBinary ? ".bin" : ".txt");

        if (segment.Length == 0)
        {
            return "response" + extension;
        }

        return Path.GetExtension(segment).Length > 0 ? segment : segment + extension;
    }

    private static string FromDisposition(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return string.Empty;
        }

        // RFC 5987 "filename*" carries the real (percent-encoded UTF-8) name and wins over "filename".
        var extended = Extended().Match(header);
        if (extended.Success)
        {
            try
            {
                var decoded = Sanitize(Uri.UnescapeDataString(extended.Groups["name"].Value.Trim()));
                if (decoded.Length > 0)
                {
                    return decoded;
                }
            }
            catch (UriFormatException)
            {
                // Fall back to the plain parameter below.
            }
        }

        var plain = Plain().Match(header);
        return plain.Success ? Sanitize(plain.Groups["name"].Value.Trim()) : string.Empty;
    }

    private static string LastSegment(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var segment = uri.Segments.LastOrDefault()?.Trim('/') ?? string.Empty;

        try
        {
            return Sanitize(Uri.UnescapeDataString(segment));
        }
        catch (UriFormatException)
        {
            return Sanitize(segment);
        }
    }

    /// <summary>Drops any directory part a server sent ("../x") and characters Windows will not accept.</summary>
    private static string Sanitize(string name)
    {
        var leaf = name.Replace('\\', '/').Split('/').LastOrDefault() ?? string.Empty;
        var invalid = Path.GetInvalidFileNameChars();
        return new string([.. leaf.Where(c => !invalid.Contains(c))]).Trim().TrimEnd('.');
    }
}
