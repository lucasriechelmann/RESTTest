using System.IO;

namespace RESTTest.Services;

/// <summary>
/// Guesses a Content-Type for an uploaded file from its extension. A short table on purpose:
/// anything not listed is sent as application/octet-stream, which servers treat as "raw bytes".
/// </summary>
public static class MimeTypes
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".js"] = "text/javascript",
        [".css"] = "text/css",
        [".pdf"] = "application/pdf",
        [".zip"] = "application/zip",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".mp3"] = "audio/mpeg",
        [".mp4"] = "video/mp4",
    };

    /// <summary>The extension to give a saved response of this type, or null when there is no good guess.</summary>
    public static string? ExtensionFor(string? contentType)
    {
        var media = (contentType ?? string.Empty).Split(';')[0].Trim().ToLowerInvariant();

        if (media.Length == 0)
        {
            return null;
        }

        // Several extensions map to one type (".jpg", ".jpeg"): the first one listed wins.
        foreach (var (extension, type) in ByExtension)
        {
            if (type == media)
            {
                return extension;
            }
        }

        return media switch
        {
            "text/xml" => ".xml",
            "application/javascript" => ".js",
            _ when media.EndsWith("+json", StringComparison.Ordinal) => ".json",
            _ when media.EndsWith("+xml", StringComparison.Ordinal) => ".xml",
            _ => null,
        };
    }

    public const string Fallback = "application/octet-stream";

    public static string For(string path) =>
        ByExtension.TryGetValue(Path.GetExtension(path), out var type) ? type : Fallback;
}
