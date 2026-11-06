using System.Text.Json.Nodes;

namespace RESTTest.Services.Importing;

/// <summary>
/// Forgiving reads for files written by other tools: a missing or wrongly typed member is just
/// absent, never an exception, because an importer should salvage what it can.
/// </summary>
internal static class JsonNodeExtensions
{
    /// <summary>The member as text. Numbers and booleans are returned in their JSON spelling.</summary>
    public static string? Str(this JsonNode? node, string name)
    {
        if (node is JsonObject obj && obj[name] is JsonValue value)
        {
            return value.TryGetValue<string>(out var text) ? text : value.ToJsonString().Trim('"');
        }

        return null;
    }

    public static JsonArray? Arr(this JsonNode? node, string name) => (node as JsonObject)?[name] as JsonArray;

    public static JsonObject? Obj(this JsonNode? node, string name) => (node as JsonObject)?[name] as JsonObject;

    public static bool Flag(this JsonNode? node, string name) =>
        node is JsonObject obj && obj[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    /// <summary>The objects of an array, skipping anything else in it.</summary>
    public static IEnumerable<JsonObject> Objects(this JsonArray? array) =>
        array is null ? [] : array.OfType<JsonObject>();
}
