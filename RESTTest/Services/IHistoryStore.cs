using RESTTest.Models;

namespace RESTTest.Services;

public interface IHistoryStore
{
    /// <summary>Newest first. Empty when there is no file or it cannot be read.</summary>
    IReadOnlyList<HistoryEntryModel> Load();

    void Save(IReadOnlyList<HistoryEntryModel> entries);
}
