namespace RESTTest.Models;

/// <summary>Where an API key travels: as a request header, or as a query-string parameter.</summary>
public enum ApiKeyLocation
{
    Header = 0,
    Query = 1,
}
