using System.Text.Json.Nodes;
using RESTTest.Models;

namespace RESTTest.Services.Importing;

/// <summary>Reads a Postman collection (v2.0 or v2.1 JSON, as exported from the app).</summary>
internal static class PostmanImporter
{
    public static bool Matches(JsonObject root) => root.Obj("info") is not null && root.Arr("item") is not null;

    public static ImportedCollection Import(JsonObject root)
    {
        var result = new ImportedCollection { Format = "Postman", Name = root.Obj("info").Str("name") ?? "Postman collection" };

        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var variable in root.Arr("variable").Objects())
        {
            if (variable.Str("key") is { Length: > 0 } key && !variable.Flag("disabled"))
            {
                variables[key] = variable.Str("value") ?? string.Empty;
            }
        }

        var context = new Context(new PlaceholderResolver(variables));
        result.Items = ConvertItems(root.Arr("item"), root["auth"] as JsonObject, context);

        // Variables that hold a URL are the collection's own idea of a base URL.
        var known = variables
            .Where(v => Uri.TryCreate(v.Value, UriKind.Absolute, out var uri)
                        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            .Select(v => new BaseUrlGrouper.KnownBase(v.Key, v.Value))
            .ToList();

        BaseUrlGrouper.Apply(result, known);
        context.AddWarnings(result.Warnings);
        return result;
    }

    private sealed class Context(PlaceholderResolver placeholders)
    {
        public PlaceholderResolver Placeholders { get; } = placeholders;

        public int WithScripts { get; set; }

        public int FileBodies { get; set; }

        public Dictionary<string, int> UnsupportedAuth { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void AddWarnings(List<string> warnings)
        {
            if (Placeholders.Describe() is { } text)
            {
                warnings.Add(text);
            }

            if (WithScripts > 0)
            {
                warnings.Add($"{WithScripts} request{(WithScripts == 1 ? " has" : "s have")} pre-request or test scripts. " +
                             "Scripts are not imported; recreate the checks on the Tests tab.");
            }

            if (FileBodies > 0)
            {
                warnings.Add($"{FileBodies} request{(FileBodies == 1 ? " sends" : "s send")} a binary file as the whole body, which RESTTest cannot do yet. The body was left empty.");
            }

            foreach (var (type, count) in UnsupportedAuth)
            {
                warnings.Add($"{count} request{(count == 1 ? " uses" : "s use")} {type} authentication, which RESTTest does not support. " +
                             "Add the credentials as a header instead.");
            }
        }
    }

    private static List<NodeModel> ConvertItems(JsonArray? items, JsonObject? inheritedAuth, Context context)
    {
        var nodes = new List<NodeModel>();

        foreach (var item in items.Objects())
        {
            var name = item.Str("name") ?? string.Empty;

            if (item.Arr("item") is { } children)
            {
                var auth = item["auth"] as JsonObject ?? inheritedAuth;
                nodes.Add(new NodeModel
                {
                    Kind = NodeKind.Folder,
                    Name = name.Length == 0 ? "Folder" : name,
                    Children = ConvertItems(children, auth, context),
                });
                continue;
            }

            if (item["event"] is JsonArray events && events.Objects().Any(HasScript))
            {
                context.WithScripts++;
            }

            nodes.Add(new NodeModel
            {
                Kind = NodeKind.Request,
                Name = name.Length == 0 ? "Request" : name,
                Request = ConvertRequest(item["request"], item["request"].Obj("auth") ?? item["auth"] as JsonObject ?? inheritedAuth, context),
            });
        }

        return nodes;
    }

    /// <summary>True when an event (pre-request or test) holds actual script text, not just an empty editor.</summary>
    private static bool HasScript(JsonObject ev) => ev.Obj("script")?["exec"] switch
    {
        JsonArray lines => lines.Any(l => !string.IsNullOrWhiteSpace(l?.ToString())),
        JsonValue value => !string.IsNullOrWhiteSpace(value.ToString()),
        _ => false,
    };

    private static RequestModel ConvertRequest(JsonNode? node, JsonObject? auth, Context context)
    {
        var request = new RequestModel { BodyKind = BodyKind.None };

        if (node is JsonValue plain && plain.TryGetValue<string>(out var urlOnly))
        {
            request.Path = StripQuery(context.Placeholders.Resolve(urlOnly), request);
            return request;
        }

        if (node is not JsonObject source)
        {
            return request;
        }

        request.Method = (source.Str("method") ?? "GET").Trim().ToUpperInvariant();
        ApplyUrl(source["url"], request, context);

        foreach (var header in source.Arr("header").Objects())
        {
            if (header.Str("key") is { Length: > 0 } key)
            {
                request.Headers.Add(new KeyValueModel
                {
                    Enabled = !header.Flag("disabled"),
                    Key = key,
                    Value = context.Placeholders.Resolve(header.Str("value")),
                });
            }
        }

        ApplyBody(source.Obj("body"), request, context);
        ApplyAuth(auth, request, context);
        return request;
    }

    private static void ApplyUrl(JsonNode? url, RequestModel request, Context context)
    {
        string raw;
        JsonArray? query = null;
        JsonArray? pathVariables = null;

        if (url is JsonValue text && text.TryGetValue<string>(out var asText))
        {
            raw = asText;
        }
        else if (url is JsonObject obj)
        {
            raw = obj.Str("raw") ?? BuildFromParts(obj);
            query = obj.Arr("query");
            pathVariables = obj.Arr("variable");
        }
        else
        {
            return;
        }

        raw = context.Placeholders.Resolve(raw);

        // Path variables (":id") are filled in where the file gives a value for them.
        foreach (var variable in pathVariables.Objects())
        {
            if (variable.Str("key") is { Length: > 0 } key && variable.Str("value") is { Length: > 0 } value)
            {
                raw = raw.Replace("/:" + key, "/" + context.Placeholders.Resolve(value), StringComparison.Ordinal);
            }
        }

        var path = StripQuery(raw, request, readQuery: query is null);

        if (query is not null)
        {
            foreach (var parameter in query.Objects())
            {
                if (parameter.Str("key") is { Length: > 0 } key)
                {
                    request.QueryParams.Add(new KeyValueModel
                    {
                        Enabled = !parameter.Flag("disabled"),
                        Key = key,
                        Value = context.Placeholders.Resolve(parameter.Str("value")),
                    });
                }
            }
        }

        request.Path = path;
    }

    private static string BuildFromParts(JsonObject url)
    {
        static string Join(JsonNode? node, string separator) => node switch
        {
            JsonArray array => string.Join(separator, array.Select(p => p is JsonObject o ? o.Str("value") : p?.ToString())),
            JsonValue value => value.ToString(),
            _ => string.Empty,
        };

        var host = Join(url["host"], ".");
        var port = url.Str("port");
        var path = Join(url["path"], "/");
        var protocol = url.Str("protocol");

        var prefix = protocol is { Length: > 0 } ? protocol + "://" : string.Empty;
        return prefix + host + (string.IsNullOrEmpty(port) ? string.Empty : ":" + port) + (path.Length == 0 ? string.Empty : "/" + path);
    }

    /// <summary>Cuts the query string and fragment off the URL, turning the query into rows when asked.</summary>
    private static string StripQuery(string url, RequestModel request, bool readQuery = true)
    {
        var hash = url.IndexOf('#');
        if (hash >= 0)
        {
            url = url[..hash];
        }

        var question = url.IndexOf('?');
        if (question < 0)
        {
            return url;
        }

        if (readQuery)
        {
            foreach (var pair in url[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = pair.IndexOf('=');
                var key = equals < 0 ? pair : pair[..equals];
                var value = equals < 0 ? string.Empty : pair[(equals + 1)..];
                request.QueryParams.Add(new KeyValueModel { Key = Decode(key), Value = Decode(value) });
            }
        }

        return url[..question];
    }

    private static string Decode(string text)
    {
        try
        {
            return Uri.UnescapeDataString(text.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return text;
        }
    }

    private static void ApplyBody(JsonObject? body, RequestModel request, Context context)
    {
        if (body is null)
        {
            return;
        }

        switch (body.Str("mode"))
        {
            case "raw":
                var text = context.Placeholders.Resolve(body.Str("raw"));
                var language = body.Obj("options").Obj("raw").Str("language");
                request.Body = text;
                request.BodyKind = RawKind(language, text, request.Headers);
                break;

            case "urlencoded":
                request.BodyKind = BodyKind.FormUrlEncoded;
                foreach (var field in body.Arr("urlencoded").Objects())
                {
                    if (field.Str("key") is { Length: > 0 } key)
                    {
                        request.FormFields.Add(new KeyValueModel
                        {
                            Enabled = !field.Flag("disabled"),
                            Key = key,
                            Value = context.Placeholders.Resolve(field.Str("value")),
                        });
                    }
                }

                break;

            case "formdata":
                request.BodyKind = BodyKind.Multipart;
                foreach (var part in body.Arr("formdata").Objects())
                {
                    if (part.Str("key") is not { Length: > 0 } key)
                    {
                        continue;
                    }

                    var isFile = part.Str("type") == "file";
                    var source = part["src"] is JsonArray files ? files.FirstOrDefault()?.ToString() : part.Str("src");

                    request.MultipartParts.Add(new MultipartPartModel
                    {
                        Enabled = !part.Flag("disabled"),
                        Key = key,
                        IsFile = isFile,
                        Value = isFile ? source ?? string.Empty : context.Placeholders.Resolve(part.Str("value")),
                    });
                }

                break;

            case "graphql":
                // GraphQL over HTTP is a JSON POST body of {query, variables}.
                var graphql = body.Obj("graphql");
                var payload = new JsonObject { ["query"] = context.Placeholders.Resolve(graphql.Str("query")) };
                var variablesText = graphql.Str("variables");
                payload["variables"] = !string.IsNullOrWhiteSpace(variablesText) && TryParse(variablesText) is { } parsed
                    ? parsed
                    : new JsonObject();

                request.BodyKind = BodyKind.Json;
                request.Body = JsonText.TryFormat(payload.ToJsonString(), out var pretty, out _) ? pretty : payload.ToJsonString();
                break;

            case "file":
                context.FileBodies++;
                break;
        }
    }

    private static JsonNode? TryParse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static BodyKind RawKind(string? language, string text, List<KeyValueModel> headers)
    {
        switch (language?.ToLowerInvariant())
        {
            case "json":
                return BodyKind.Json;
            case "xml":
                return BodyKind.Xml;
            case "text" or "html" or "javascript":
                return BodyKind.Text;
        }

        var contentType = headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
        {
            return BodyKind.Json;
        }

        if (contentType.Contains("xml", StringComparison.OrdinalIgnoreCase))
        {
            return BodyKind.Xml;
        }

        return TryParse(text) is JsonObject or JsonArray ? BodyKind.Json : BodyKind.Text;
    }

    private static void ApplyAuth(JsonObject? auth, RequestModel request, Context context)
    {
        var type = auth.Str("type")?.ToLowerInvariant();

        switch (type)
        {
            case null or "" or "noauth" or "inherit":
                return;

            case "bearer":
                request.Auth = AuthKind.BearerToken;
                request.BearerToken = context.Placeholders.Resolve(AuthValue(auth, "bearer", "token"));
                return;

            case "basic":
                request.Auth = AuthKind.Basic;
                request.BasicUsername = context.Placeholders.Resolve(AuthValue(auth, "basic", "username"));
                request.BasicPassword = context.Placeholders.Resolve(AuthValue(auth, "basic", "password"));
                return;

            case "apikey":
                request.Auth = AuthKind.ApiKey;
                request.ApiKeyName = context.Placeholders.Resolve(AuthValue(auth, "apikey", "key"));
                request.ApiKeyValue = context.Placeholders.Resolve(AuthValue(auth, "apikey", "value"));
                request.ApiKeyLocation = string.Equals(AuthValue(auth, "apikey", "in"), "query", StringComparison.OrdinalIgnoreCase)
                    ? ApiKeyLocation.Query
                    : ApiKeyLocation.Header;
                return;

            default:
                context.UnsupportedAuth[type] = context.UnsupportedAuth.GetValueOrDefault(type) + 1;
                return;
        }
    }

    /// <summary>Postman v2.1 stores auth settings as [{key, value}], v2.0 as a plain object.</summary>
    private static string? AuthValue(JsonObject? auth, string type, string key)
    {
        var node = auth?[type];

        if (node is JsonArray entries)
        {
            return entries.Objects().FirstOrDefault(e => e.Str("key") == key).Str("value");
        }

        return node.Str(key);
    }
}
