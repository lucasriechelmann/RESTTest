using System.Text.Json;
using System.Text.Json.Nodes;

namespace RESTTest.Services.Importing;

/// <summary>
/// Recognises a Postman, Insomnia or OpenAPI/Swagger JSON file and turns it into a collection.
/// Pure string-in, model-out, so it is testable without a window or a file.
/// </summary>
public static class CollectionImporter
{
    public const string FileFilter = "Postman, Insomnia or OpenAPI (*.json)|*.json|All files (*.*)|*.*";

    public static bool TryImport(string? text, out ImportedCollection? result, out string? error)
    {
        result = null;
        error = null;

        var content = (text ?? string.Empty).TrimStart('﻿', ' ', '\t', '\r', '\n');
        if (content.Length == 0)
        {
            error = "The file is empty.";
            return false;
        }

        if (content[0] != '{')
        {
            error = LooksLikeYaml(content)
                ? "This looks like a YAML file. RESTTest reads JSON only for now: open the file in its own tool " +
                  "(Insomnia, Swagger Editor) and export or convert it to JSON, then import that."
                : "This is not a JSON file, so it is not a Postman, Insomnia or OpenAPI export.";
            return false;
        }

        JsonObject root;
        try
        {
            root = JsonNode.Parse(content, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            }) as JsonObject ?? throw new JsonException("The top level is not an object.");
        }
        catch (JsonException ex)
        {
            error = $"The file is not valid JSON: {ex.Message}";
            return false;
        }

        try
        {
            if (OpenApiImporter.Matches(root))
            {
                result = OpenApiImporter.Import(root);
            }
            else if (InsomniaImporter.Matches(root))
            {
                result = InsomniaImporter.Import(root);
            }
            else if (PostmanImporter.Matches(root))
            {
                result = PostmanImporter.Import(root);
            }
            else
            {
                error = Unrecognised(root);
                return false;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException or JsonException)
        {
            error = $"The file was recognised but could not be read: {ex.Message}";
            return false;
        }

        if (result.RequestCount == 0)
        {
            result = null;
            error = "The file contains no requests to import.";
            return false;
        }

        return true;
    }

    private static string Unrecognised(JsonObject root)
    {
        if (root["_postman_variable_scope"] is not null)
        {
            return "This is a Postman environment file, not a collection. Export the collection itself " +
                   "(its \"Export\" menu, Collection v2.1).";
        }

        if (root["requests"] is JsonArray && root["item"] is null)
        {
            return "This looks like a Postman v1 collection. Re-export it from Postman as Collection v2.1.";
        }

        return "This file is not a Postman v2 collection, an Insomnia v4 export or an OpenAPI/Swagger JSON document.";
    }

    private static bool LooksLikeYaml(string content)
    {
        var head = content[..Math.Min(content.Length, 600)];
        return head.Contains("openapi:", StringComparison.Ordinal)
               || head.Contains("swagger:", StringComparison.Ordinal)
               || head.Contains("_type:", StringComparison.Ordinal)
               || head.StartsWith("---", StringComparison.Ordinal)
               || head.Contains("\ninfo:", StringComparison.Ordinal);
    }
}
