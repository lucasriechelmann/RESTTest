using RESTTest.Models;

namespace RESTTest.Services;

public interface ICollectionStore
{
    /// <summary>Folder the JSON files are read from and written to.</summary>
    string WorkspacePath { get; }

    IReadOnlyList<CollectionModel> LoadAll();

    void Save(CollectionModel collection);

    void Delete(string collectionId);

    AppSettings LoadSettings();

    void SaveSettings(AppSettings settings);
}
