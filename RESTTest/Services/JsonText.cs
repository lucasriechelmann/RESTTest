using System.IO;
using System.Text;
using System.Text.Json;

namespace RESTTest.Services;

/// <summary>Pretty-printing and validation helpers for the JSON body/response editors.</summary>
public static class JsonText
{
    private static readonly JsonDocumentOptions ReaderOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Re-writes <paramref name="input"/> with two-space indentation. Returns false and a
    /// human readable reason when the text is not valid JSON, leaving the caller's text untouched.
    /// </summary>
    public static bool TryFormat(string? input, out string formatted, out string? error)
        => TryWrite(input, indented: true, out formatted, out error);

    /// <summary>Collapses the JSON onto a single line.</summary>
    public static bool TryMinify(string? input, out string minified, out string? error)
        => TryWrite(input, indented: false, out minified, out error);

    /// <summary>
    /// Best-effort pretty print used for the response pane: returns the original text unchanged
    /// when the payload is not JSON (HTML error pages, plain text, and so on).
    /// </summary>
    public static string PrettyOrOriginal(string? input)
        => TryFormat(input, out var formatted, out _) ? formatted : input ?? string.Empty;

    public static bool IsValid(string? input) => TryFormat(input, out _, out _);

    private static bool TryWrite(string? input, bool indented, out string output, out string? error)
    {
        output = input ?? string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "There is nothing to format.";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(input, ReaderOptions);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
            {
                Indented = indented,
                IndentCharacter = ' ',
                IndentSize = 2,
                // Keep payloads readable: don't escape non-ASCII or characters like '+' and '&'.
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            }))
            {
                document.WriteTo(writer);
            }

            output = Encoding.UTF8.GetString(buffer.ToArray());
            return true;
        }
        catch (JsonException ex)
        {
            // JsonException carries the offending line/position, which is the useful part.
            error = ex.LineNumber is { } line
                ? $"Invalid JSON at line {line + 1}, position {ex.BytePositionInLine + 1}."
                : $"Invalid JSON: {ex.Message}";
            return false;
        }
    }
}
