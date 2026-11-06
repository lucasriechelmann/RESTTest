using System.IO;
using System.Text.Json;
using RESTTest.Models;

namespace RESTTest.Services;

/// <summary>Keeps the cookie jar in one <c>cookies.json</c> next to the collections.</summary>
public sealed class CookieStore : ICookieStore
{
    private readonly string _file;

    public CookieStore(string workspacePath)
    {
        _file = Path.Combine(workspacePath, "cookies.json");
    }

    public CookieJarModel Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                return JsonSerializer.Deserialize<CookieJarModel>(File.ReadAllText(_file), JsonFile.Options) ?? new CookieJarModel();
            }
        }
        catch (Exception)
        {
            // Cookies are a convenience. A damaged file must never stop the app from starting;
            // the next save replaces it.
        }

        return new CookieJarModel();
    }

    public void Save(CookieJarModel jar)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        JsonFile.WriteAtomic(_file, JsonSerializer.Serialize(jar, JsonFile.Options));
    }
}
