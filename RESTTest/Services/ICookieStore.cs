using RESTTest.Models;

namespace RESTTest.Services;

public interface ICookieStore
{
    /// <summary>The saved jar, or an empty enabled one when there is no file or it cannot be read.</summary>
    CookieJarModel Load();

    void Save(CookieJarModel jar);
}
