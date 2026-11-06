namespace RESTTest.Models;

/// <summary>Persisted form of one part of a multipart/form-data body: a text field or a file.</summary>
public sealed class MultipartPartModel
{
    public bool Enabled { get; set; } = true;

    public string Key { get; set; } = string.Empty;

    /// <summary>The text to send, or the path of the file to upload when <see cref="IsFile"/> is set.</summary>
    public string Value { get; set; } = string.Empty;

    public bool IsFile { get; set; }
}
