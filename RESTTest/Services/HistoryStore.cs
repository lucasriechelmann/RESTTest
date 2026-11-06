using System.IO;
using System.Text.Json;
using RESTTest.Models;

namespace RESTTest.Services;

/// <summary>Keeps the request history in one <c>history.json</c> next to the collections.</summary>
public sealed class HistoryStore : IHistoryStore
{
    private readonly string _file;

    public HistoryStore(string workspacePath)
    {
        _file = Path.Combine(workspacePath, "history.json");
    }

    public IReadOnlyList<HistoryEntryModel> Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                return JsonSerializer.Deserialize<List<HistoryEntryModel>>(File.ReadAllText(_file), JsonFile.Options)
                       ?? [];
            }
        }
        catch (Exception)
        {
            // History is a convenience. A damaged file must never stop the app from starting;
            // it is simply replaced by the next save.
        }

        return [];
    }

    public void Save(IReadOnlyList<HistoryEntryModel> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        JsonFile.WriteAtomic(_file, JsonSerializer.Serialize(entries, JsonFile.Options));
    }
}
