using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RESTTest.Services;

/// <summary>
/// The JSON settings and the atomic write shared by every file the app keeps in the workspace
/// (collections, settings, history), so they cannot drift apart.
/// </summary>
public static class JsonFile
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        // Enums as names ("GET", "BearerToken") rather than opaque integers.
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // The default encoder escapes quotes as ", which makes an embedded JSON body
        // unreadable. These files are local and never served as HTML, so relaxed escaping is safe.
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Writes via a temp file and a replace, so a crash mid-save cannot leave a truncated file
    /// behind. Auto-save runs often, which makes that a realistic failure.
    /// </summary>
    public static void WriteAtomic(string path, string contents)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);

        if (File.Exists(path))
        {
            File.Replace(temp, path, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temp, path);
        }
    }
}
