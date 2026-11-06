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

    public const string Fallback = "application/octet-stream";

    public static string For(string path) =>
        ByExtension.TryGetValue(Path.GetExtension(path), out var type) ? type : Fallback;
}
