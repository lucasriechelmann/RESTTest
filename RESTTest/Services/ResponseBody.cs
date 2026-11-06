using System.IO;
using System.Net.Http.Headers;
using System.Text;

namespace RESTTest.Services;

/// <summary>
/// Turns the bytes of a response into text for the body viewer, or decides they are not text at
/// all. A PDF or an image pushed through a UTF-8 decoder shows as garbage and cannot be saved
/// back byte-for-byte, so such a body is kept as bytes and only offered for saving.
/// </summary>
public static class ResponseBody
{
    private const int SniffLength = 8000;

    private static readonly string[] TextMarkers =
    [
        "json", "xml", "javascript", "ecmascript", "html", "yaml", "csv", "graphql", "x-www-form-urlencoded", "x-ndjson",
    ];

    private static readonly string[] BinaryMarkers =
    [
        "octet-stream", "pdf", "zip", "gzip", "x-tar", "x-7z", "x-rar", "msword", "vnd.", "x-msdownload", "wasm",
    ];

    /// <summary>The text of the body (empty for binary content), and whether it was judged binary.</summary>
    public static string Decode(byte[] bytes, string? contentType, out bool isBinary)
    {
        isBinary = IsBinary(bytes, contentType);

        if (isBinary || bytes.Length == 0)
        {
            return string.Empty;
        }

        // A byte-order mark wins over the declared charset, as it does in a browser.
        using var reader = new StreamReader(new MemoryStream(bytes), ResolveEncoding(contentType), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static bool IsBinary(byte[] bytes, string? contentType)
    {
        var media = MediaType(contentType);

        if (media.Length > 0)
        {
            if (media.StartsWith("text/", StringComparison.Ordinal) || TextMarkers.Any(media.Contains))
            {
                return false;
            }

            if (media.StartsWith("image/", StringComparison.Ordinal)
                || media.StartsWith("audio/", StringComparison.Ordinal)
                || media.StartsWith("video/", StringComparison.Ordinal)
                || media.StartsWith("font/", StringComparison.Ordinal)
                || BinaryMarkers.Any(media.Contains))
            {
                return true;
            }
        }

        // Unknown or missing type: look at the bytes. A NUL or invalid UTF-8 means it is not text.
        var head = bytes.AsSpan(0, Math.Min(bytes.Length, SniffLength));
        if (head.Contains((byte)0))
        {
            return true;
        }

        return !IsValidUtf8(head);
    }

    private static bool IsValidUtf8(ReadOnlySpan<byte> head)
    {
        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);

        try
        {
            _ = strict.GetString(head);
            return true;
        }
        catch (DecoderFallbackException)
        {
            // The sniff window may end in the middle of a multi-byte character. Only a failure
            // that persists without the last three bytes is real.
            if (head.Length < SniffLength)
            {
                return false;
            }

            try
            {
                _ = strict.GetString(head[..^3]);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }
    }

    private static string MediaType(string? contentType) =>
        (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();

    private static Encoding ResolveEncoding(string? contentType)
    {
        if (MediaTypeHeaderValue.TryParse(contentType, out var parsed) && parsed.CharSet is { Length: > 0 } charset)
        {
            try
            {
                return Encoding.GetEncoding(charset.Trim('"', '\''));
            }
            catch (ArgumentException)
            {
                // Unknown charset name: fall through to UTF-8, which is what most servers mean anyway.
            }
        }

        return Encoding.UTF8;
    }
}
