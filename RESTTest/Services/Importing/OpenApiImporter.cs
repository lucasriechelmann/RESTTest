using System.Text.Json.Nodes;
using RESTTest.Models;

namespace RESTTest.Services.Importing;

/// <summary>
/// Reads an OpenAPI 3.x or Swagger 2.0 document (JSON). Each operation becomes a request, grouped
/// into a folder per first tag; the servers become the collection's base URLs.
/// </summary>
internal static class OpenApiImporter
{
    private static readonly string[] Methods = ["get", "put", "post", "delete", "options", "head", "patch"];

    private static readonly HashSet<string> SkippedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Content-Type", "Accept",
    };

    public static bool Matches(JsonObject root) =>
        (root.Str("openapi") is not null || root.Str("swagger") is not null) && root.Obj("paths") is not null;

    public static ImportedCollection Import(JsonObject root)
    {
        var result = new ImportedCollection { Format = "OpenAPI", Name = root.Obj("info").Str("title") ?? "OpenAPI document" };
        var isSwagger2 = root.Str("swagger") is not null;
        var notes = new Notes();

        AddServers(root, isSwagger2, result);

        var folders = new Dictionary<string, NodeModel>(StringComparer.Ordinal);
        var paths = root.Obj("paths")!;

        foreach (var (path, pathNode) in paths)
        {
            if (pathNode is not JsonObject pathItem)
            {
                continue;
            }

            foreach (var method in Methods)
            {
                if (pathItem[method] is not JsonObject operation)
                {
                    continue;
                }

                var request = BuildRequest(root, isSwagger2, path, method, pathItem, operation, notes);
                var name = operation.Str("summary") ?? operation.Str("operationId") ?? $"{method.ToUpperInvariant()} {path}";
                var node = new NodeModel { Kind = NodeKind.Request, Name = name, Request = request };

                var tag = operation.Arr("tags")?.FirstOrDefault()?.ToString();
                if (string.IsNullOrWhiteSpace(tag))
                {
                    result.Items.Add(node);
                    continue;
                }

                if (!folders.TryGetValue(tag, out var folder))
                {
                    folder = new NodeModel { Kind = NodeKind.Folder, Name = tag };
                    folders[tag] = folder;
                    result.Items.Add(folder);
                }

                folder.Children.Add(node);
            }
        }

        notes.AddWarnings(result.Warnings);
        return result;
    }

    private sealed class Notes
    {
        public int PathParameters { get; set; }

        public int CookieParameters { get; set; }

        public HashSet<string> UnsupportedSecurity { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void AddWarnings(List<string> warnings)
        {
            if (PathParameters > 0)
            {
                warnings.Add($"{PathParameters} request{(PathParameters == 1 ? " has" : "s have")} path parameters such as {{id}} in the path. " +
                             "Replace them with real values before sending.");
            }

            if (CookieParameters > 0)
            {
                warnings.Add($"{CookieParameters} cookie parameter{(CookieParameters == 1 ? " was" : "s were")} skipped; add a Cookie header if you need one.");
            }

            if (UnsupportedSecurity.Count > 0)
            {
                warnings.Add($"The security scheme{(UnsupportedSecurity.Count == 1 ? string.Empty : "s")} {string.Join(", ", UnsupportedSecurity)} " +
                             "(OAuth 2 / OpenID Connect) cannot be configured here. Add the access token as a bearer token or header.");
            }
        }
    }

    private static void AddServers(JsonObject root, bool isSwagger2, ImportedCollection result)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string name, string url)
        {
            var candidate = string.IsNullOrWhiteSpace(name) ? "Server" : name.Trim();
            for (var suffix = 2; !used.Add(candidate); suffix++)
            {
                candidate = $"{name.Trim()} {suffix}";
            }

            result.Environments.Add(new EnvironmentModel { Name = candidate, BaseUrl = url.TrimEnd('/') });
        }

        if (isSwagger2)
        {
            var host = root.Str("host");
            if (!string.IsNullOrWhiteSpace(host))
            {
                var scheme = root.Arr("schemes")?.Select(s => s?.ToString()).FirstOrDefault(s => s is "https" or "http") ?? "https";
                Add(host, $"{scheme}://{host}{root.Str("basePath")?.TrimEnd('/')}");
            }
        }
        else
        {
            foreach (var server in root.Arr("servers").Objects())
            {
                var url = server.Str("url") ?? string.Empty;

                // {variable} placeholders take their declared default, the only value a file guarantees.
                foreach (var (variable, definition) in server.Obj("variables") ?? [])
                {
                    url = url.Replace("{" + variable + "}", definition.Str("default") ?? string.Empty, StringComparison.Ordinal);
                }

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                {
                    result.Warnings.Add($"The server \"{url}\" is not a full URL (the document is meant to be served from its own host). " +
                                        "Add the real host under Manage URLs.");
                    Add(url.Length == 0 ? "Server" : url, string.Empty);
                    continue;
                }

                Add(server.Str("description") is { Length: > 0 } description && description.Length <= 40 ? description : uri.Host, url);
            }
        }

        if (result.Environments.Count == 0)
        {
            result.Warnings.Add("The document lists no servers. Add the API's host under Manage URLs before sending.");
            Add("Server", string.Empty);
        }
    }

    private static RequestModel BuildRequest(
        JsonObject root,
        bool isSwagger2,
        string path,
        string method,
        JsonObject pathItem,
        JsonObject operation,
        Notes notes)
    {
        var request = new RequestModel { Method = method.ToUpperInvariant(), Path = path, BodyKind = BodyKind.None };

        if (path.Contains('{', StringComparison.Ordinal))
        {
            notes.PathParameters++;
        }

        // Operation-level parameters override path-level ones with the same name and location.
        var parameters = new List<JsonObject>();
        foreach (var parameter in Resolve(root, pathItem.Arr("parameters")).Concat(Resolve(root, operation.Arr("parameters"))))
        {
            parameters.RemoveAll(p => p.Str("name") == parameter.Str("name") && p.Str("in") == parameter.Str("in"));
            parameters.Add(parameter);
        }

        JsonObject? swaggerBody = null;
        var formData = new List<JsonObject>();

        foreach (var parameter in parameters)
        {
            var name = parameter.Str("name") ?? string.Empty;
            var required = parameter.Flag("required");

            switch (parameter.Str("in"))
            {
                case "query" when name.Length > 0:
                    request.QueryParams.Add(new KeyValueModel { Enabled = required, Key = name, Value = ScalarExample(root, parameter) });
                    break;

                case "header" when name.Length > 0 && !SkippedHeaders.Contains(name):
                    request.Headers.Add(new KeyValueModel { Enabled = required, Key = name, Value = ScalarExample(root, parameter) });
                    break;

                case "cookie":
                    notes.CookieParameters++;
                    break;

                case "body":
                    swaggerBody = parameter;
                    break;

                case "formData":
                    formData.Add(parameter);
                    break;
            }
        }

        if (isSwagger2)
        {
            ApplySwagger2Body(root, operation, swaggerBody, formData, request);
        }
        else
        {
            ApplyRequestBody(root, Resolve(root, operation.Obj("requestBody")), request);
        }

        ApplySecurity(root, operation, isSwagger2, request, notes);
        return request;
    }

    // ---- Bodies ----------------------------------------------------------------------------

    private static void ApplyRequestBody(JsonObject root, JsonObject? body, RequestModel request)
    {
        var content = body.Obj("content");
        if (content is null || content.Count == 0)
        {
            return;
        }

        var type = content.Select(c => c.Key).FirstOrDefault(k => k.Contains("json", StringComparison.OrdinalIgnoreCase))
                   ?? content.Select(c => c.Key).FirstOrDefault(k => k.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
                   ?? content.Select(c => c.Key).FirstOrDefault(k => k.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
                   ?? content.Select(c => c.Key).FirstOrDefault(k => k.Contains("xml", StringComparison.OrdinalIgnoreCase))
                   ?? content.First().Key;

        var media = content[type] as JsonObject;
        var schema = Resolve(root, media.Obj("schema"));

        if (type.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            var example = ExampleOf(root, media) ?? Example(root, schema, 0);
            request.BodyKind = BodyKind.Json;
            request.Body = Pretty(example);
        }
        else if (type.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            request.BodyKind = BodyKind.FormUrlEncoded;
            foreach (var (name, property) in Properties(root, schema))
            {
                request.FormFields.Add(new KeyValueModel { Key = name, Value = ScalarText(Example(root, property, 0)) });
            }
        }
        else if (type.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            request.BodyKind = BodyKind.Multipart;
            foreach (var (name, property) in Properties(root, schema))
            {
                var isFile = Resolve(root, property).Str("format") is "binary" or "byte";
                request.MultipartParts.Add(new MultipartPartModel
                {
                    Key = name,
                    IsFile = isFile,
                    Value = isFile ? string.Empty : ScalarText(Example(root, property, 0)),
                });
            }
        }
        else if (type.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            request.BodyKind = BodyKind.Xml;
            request.Body = ExampleOf(root, media)?.ToString() ?? string.Empty;
        }
        else
        {
            request.BodyKind = BodyKind.Text;
            request.Body = ExampleOf(root, media) is { } example ? ScalarText(example) : string.Empty;
        }
    }

    private static void ApplySwagger2Body(
        JsonObject root,
        JsonObject operation,
        JsonObject? bodyParameter,
        List<JsonObject> formData,
        RequestModel request)
    {
        var consumes = (operation.Arr("consumes") ?? root.Arr("consumes"))?.Select(c => c?.ToString() ?? string.Empty).ToList() ?? [];

        if (bodyParameter is not null)
        {
            var schema = Resolve(root, bodyParameter.Obj("schema"));
            var isXml = consumes.Count > 0 && !consumes.Any(c => c.Contains("json", StringComparison.OrdinalIgnoreCase))
                        && consumes.Any(c => c.Contains("xml", StringComparison.OrdinalIgnoreCase));

            request.BodyKind = isXml ? BodyKind.Xml : BodyKind.Json;
            request.Body = isXml ? string.Empty : Pretty(Example(root, schema, 0));
            return;
        }

        if (formData.Count == 0)
        {
            return;
        }

        var multipart = formData.Any(f => f.Str("type") == "file")
                        || consumes.Any(c => c.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase));

        if (multipart)
        {
            request.BodyKind = BodyKind.Multipart;
            foreach (var field in formData)
            {
                var isFile = field.Str("type") == "file";
                request.MultipartParts.Add(new MultipartPartModel
                {
                    Enabled = field.Flag("required") || !isFile,
                    Key = field.Str("name") ?? string.Empty,
                    IsFile = isFile,
                    Value = isFile ? string.Empty : ScalarExample(root, field),
                });
            }
        }
        else
        {
            request.BodyKind = BodyKind.FormUrlEncoded;
            foreach (var field in formData)
            {
                request.FormFields.Add(new KeyValueModel { Key = field.Str("name") ?? string.Empty, Value = ScalarExample(root, field) });
            }
        }
    }

    // ---- Security --------------------------------------------------------------------------

    private static void ApplySecurity(JsonObject root, JsonObject operation, bool isSwagger2, RequestModel request, Notes notes)
    {
        // An operation's own "security" replaces the document's, and an empty list means "no auth".
        var requirements = operation.Arr("security") ?? root.Arr("security");
        var name = requirements.Objects().FirstOrDefault()?.Select(p => p.Key).FirstOrDefault();

        if (name is null)
        {
            return;
        }

        var schemes = isSwagger2 ? root.Obj("securityDefinitions") : root.Obj("components").Obj("securitySchemes");
        var scheme = Resolve(root, schemes.Obj(name));
        if (scheme is null)
        {
            return;
        }

        var type = scheme.Str("type")?.ToLowerInvariant();

        switch (type)
        {
            case "http" when string.Equals(scheme.Str("scheme"), "bearer", StringComparison.OrdinalIgnoreCase):
                request.Auth = AuthKind.BearerToken;
                break;

            case "http" when string.Equals(scheme.Str("scheme"), "basic", StringComparison.OrdinalIgnoreCase):
            case "basic":
                request.Auth = AuthKind.Basic;
                break;

            case "apikey":
                if (scheme.Str("in") is "header" or "query")
                {
                    request.Auth = AuthKind.ApiKey;
                    request.ApiKeyName = scheme.Str("name") ?? string.Empty;
                    request.ApiKeyLocation = scheme.Str("in") == "query" ? ApiKeyLocation.Query : ApiKeyLocation.Header;
                }
                else
                {
                    notes.CookieParameters++;
                }

                break;

            default:
                notes.UnsupportedSecurity.Add(name);
                break;
        }
    }

    // ---- Examples and $ref -----------------------------------------------------------------

    /// <summary>Follows a local <c>$ref</c> ("#/components/schemas/X"); other references are returned as written.</summary>
    private static JsonObject? Resolve(JsonObject root, JsonObject? node)
    {
        for (var hops = 0; node?.Str("$ref") is { } reference && hops < 20; hops++)
        {
            if (!reference.StartsWith("#/", StringComparison.Ordinal))
            {
                return node;
            }

            JsonNode? target = root;
            foreach (var segment in reference[2..].Split('/'))
            {
                target = (target as JsonObject)?[segment.Replace("~1", "/").Replace("~0", "~")];
            }

            node = target as JsonObject;
        }

        return node;
    }

    private static IEnumerable<JsonObject> Resolve(JsonObject root, JsonArray? array) =>
        array.Objects().Select(o => Resolve(root, o)).OfType<JsonObject>();

    private static IEnumerable<KeyValuePair<string, JsonObject>> Properties(JsonObject root, JsonObject? schema)
    {
        if (schema is null)
        {
            yield break;
        }

        foreach (var part in schema.Arr("allOf").Objects().Select(o => Resolve(root, o)))
        {
            foreach (var pair in Properties(root, part))
            {
                yield return pair;
            }
        }

        foreach (var (name, property) in schema.Obj("properties") ?? [])
        {
            if (property is JsonObject obj)
            {
                yield return new KeyValuePair<string, JsonObject>(name, obj);
            }
        }
    }

    /// <summary>An explicit example on a media type: <c>example</c>, or the first of <c>examples</c>.</summary>
    private static JsonNode? ExampleOf(JsonObject root, JsonObject? media)
    {
        if (media?["example"] is { } example)
        {
            return example.DeepClone();
        }

        var first = media.Obj("examples")?.Select(e => e.Value).OfType<JsonObject>().FirstOrDefault();
        return Resolve(root, first)?["value"]?.DeepClone();
    }

    private static string ScalarExample(JsonObject root, JsonObject parameter)
    {
        var schema = Resolve(root, parameter.Obj("schema")) ?? parameter;
        var example = parameter["example"] ?? parameter["x-example"] ?? Example(root, schema, 0, scalarsOnly: true);
        return ScalarText(example);
    }

    private static string ScalarText(JsonNode? node) => node switch
    {
        null => string.Empty,
        JsonValue value => value.TryGetValue<string>(out var text) ? text : value.ToJsonString(),
        _ => node.ToJsonString(),
    };

    private static string Pretty(JsonNode? node)
    {
        if (node is null)
        {
            return string.Empty;
        }

        var text = node.ToJsonString();
        return JsonText.TryFormat(text, out var formatted, out _) ? formatted : text;
    }

    /// <summary>
    /// A plausible value for a schema: its own example or default, else the first enum value, else a
    /// placeholder of the right type. Depth-limited, so a recursive schema cannot run away.
    /// </summary>
    private static JsonNode? Example(JsonObject root, JsonObject? schema, int depth, bool scalarsOnly = false)
    {
        schema = Resolve(root, schema);
        if (schema is null || depth > 4)
        {
            return null;
        }

        if (schema["example"] is { } example)
        {
            return example.DeepClone();
        }

        if (schema["default"] is { } fallback)
        {
            return fallback.DeepClone();
        }

        if (schema.Arr("enum") is { Count: > 0 } choices)
        {
            return choices[0]?.DeepClone();
        }

        if (schema.Arr("allOf") is { } all)
        {
            var merged = new JsonObject();
            foreach (var part in all.Objects())
            {
                if (Example(root, part, depth + 1) is JsonObject piece)
                {
                    foreach (var (key, value) in piece)
                    {
                        merged[key] = value?.DeepClone();
                    }
                }
            }

            return merged;
        }

        if ((schema.Arr("oneOf") ?? schema.Arr("anyOf"))?.OfType<JsonObject>().FirstOrDefault() is { } alternative)
        {
            return Example(root, alternative, depth + 1);
        }

        var type = schema.Str("type") ?? (schema.Obj("properties") is not null ? "object" : null);

        switch (type)
        {
            case "object":
                if (scalarsOnly)
                {
                    return null;
                }

                var obj = new JsonObject();
                foreach (var (name, property) in schema.Obj("properties") ?? [])
                {
                    obj[name] = Example(root, property as JsonObject, depth + 1);
                }

                return obj;

            case "array":
                if (scalarsOnly)
                {
                    return null;
                }

                var items = Example(root, schema.Obj("items"), depth + 1);
                return items is null ? new JsonArray() : new JsonArray(items);

            case "integer" or "number":
                return JsonValue.Create(0);

            case "boolean":
                return JsonValue.Create(false);

            case "string":
                return JsonValue.Create(schema.Str("format") switch
                {
                    "date-time" => "2024-01-01T00:00:00Z",
                    "date" => "2024-01-01",
                    "uuid" => "00000000-0000-0000-0000-000000000000",
                    "email" => "user@example.com",
                    "uri" or "url" => "https://example.com",
                    _ => "string",
                });

            default:
                return null;
        }
    }
}
