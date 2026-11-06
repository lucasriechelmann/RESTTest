using RESTTest.Models;

namespace RESTTest.Services;

public interface IHttpRequestSender
{
    /// <summary>Resolves the absolute URL a request would hit, for display in the UI.</summary>
    string BuildUrl(string? baseUrl, RequestModel request);

    Task<HttpExchangeResult> SendAsync(string? baseUrl, RequestModel request, CancellationToken cancellationToken);
}
