using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RESTTest.Models;

namespace RESTTest.Services;

/// <summary>
/// Reads and writes one indented JSON file per collection under
/// %APPDATA%\RestTest\collections, so the files stay hand-editable and diffable.
/// </summary>
public sealed class CollectionStore : ICollectionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
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

    private readonly string _collectionsPath;
    private readonly string _settingsFile;

    public CollectionStore()
        : this(workspacePath: null)
    {
    }

    /// <param name="workspacePath">
    /// Overrides the default %APPDATA%\RestTest location. Exists so tests can point the store at
    /// a temporary folder instead of touching the real workspace.
    /// </param>
    public CollectionStore(string? workspacePath)
    {
        WorkspacePath = workspacePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RestTest");

        _collectionsPath = Path.Combine(WorkspacePath, "collections");
        _settingsFile = Path.Combine(WorkspacePath, "settings.json");

        Directory.CreateDirectory(_collectionsPath);
    }

    public string WorkspacePath { get; }

    public IReadOnlyList<CollectionModel> LoadAll()
    {
        var collections = new List<CollectionModel>();

        foreach (var file in Directory.EnumerateFiles(_collectionsPath, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var collection = JsonSerializer.Deserialize<CollectionModel>(json, SerializerOptions);
                if (collection is not null)
                {
                    // A hand-edited file may be missing ids; fill them so moves stay stable.
                    EnsureIds(collection);
                    collections.Add(collection);
                }
            }
            catch (Exception)
            {
                // One unreadable file must not stop the app from opening the rest.
            }
        }

        return [.. collections.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    public void Save(CollectionModel collection)
    {
        Directory.CreateDirectory(_collectionsPath);
        var json = JsonSerializer.Serialize(collection, SerializerOptions);
        WriteAtomic(FilePathFor(collection.Id), json);
    }

    public void Delete(string collectionId)
    {
        var path = FilePathFor(collectionId);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public AppSettings LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsFile))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_settingsFile), SerializerOptions)
                       ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // Fall through to defaults: stale UI state is never worth a crash on startup.
        }

        return new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(WorkspacePath);
            WriteAtomic(_settingsFile, JsonSerializer.Serialize(settings, SerializerOptions));
        }
        catch (Exception)
        {
            // Losing window state is not worth surfacing an error for.
        }
    }

    private string FilePathFor(string collectionId)
    {
        // Keyed on the id, not the name, so renaming a collection does not orphan its file.
        var safeId = string.Concat(collectionId.Where(char.IsLetterOrDigit));
        if (safeId.Length == 0)
        {
            safeId = Guid.NewGuid().ToString("N");
        }

        return Path.Combine(_collectionsPath, safeId + ".json");
    }

    /// <summary>
    /// Writes via a temp file and a replace, so a crash mid-save cannot leave a truncated
    /// collection behind. Auto-save runs often, which makes that a realistic failure.
    /// </summary>
    private static void WriteAtomic(string path, string contents)
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

    private static void EnsureIds(CollectionModel collection)
    {
        if (string.IsNullOrWhiteSpace(collection.Id))
        {
            collection.Id = Guid.NewGuid().ToString("N");
        }

        foreach (var environment in collection.Environments)
        {
            if (string.IsNullOrWhiteSpace(environment.Id))
            {
                environment.Id = Guid.NewGuid().ToString("N");
            }
        }

        EnsureNodeIds(collection.Items);
    }

    private static void EnsureNodeIds(List<NodeModel> nodes)
    {
        foreach (var node in nodes)
        {
            if (string.IsNullOrWhiteSpace(node.Id))
            {
                node.Id = Guid.NewGuid().ToString("N");
            }

            if (node.Kind == NodeKind.Request)
            {
                node.Request ??= new RequestModel();
            }

            EnsureNodeIds(node.Children);
        }
    }
}
