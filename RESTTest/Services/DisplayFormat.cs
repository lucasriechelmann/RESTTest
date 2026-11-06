namespace RESTTest.Services;

/// <summary>
/// Text shown for response facts, shared by the response pane, the runner report and the history
/// list so a duration or size never reads differently in two places.
/// </summary>
public static class DisplayFormat
{
    public static string Duration(long milliseconds) =>
        milliseconds < 1000 ? $"{milliseconds} ms" : $"{milliseconds / 1000.0:0.00} s";

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.00} MB",
    };

    /// <summary>Drives the status pill colour: 2xx green, 3xx blue, 4xx amber, 5xx red.</summary>
    public static string StatusCategory(int statusCode) => statusCode switch
    {
        >= 200 and < 300 => "Success",
        >= 300 and < 400 => "Redirect",
        >= 400 and < 500 => "ClientError",
        >= 500 => "ServerError",
        _ => "Unknown",
    };
}
