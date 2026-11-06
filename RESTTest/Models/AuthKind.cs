namespace RESTTest.Models;

/// <remarks>Persisted by name; append new kinds rather than renaming, or old files stop loading.</remarks>
public enum AuthKind
{
    None = 0,
    BearerToken = 1,
    Basic = 2,
    ApiKey = 3,
}
