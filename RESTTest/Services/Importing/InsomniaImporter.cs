using System.Text.Json.Nodes;
using RESTTest.Models;

namespace RESTTest.Services.Importing;

/// <summary>Reads an Insomnia v4 export (JSON, <c>_type: "export"</c>). Insomnia v5 files are YAML and are not read.</summary>
internal static class InsomniaImporter
{
    public static bool Matches(JsonObject root) =>
        root.Str("_type") == "export" || root.Arr("resources") is not null;

    public static ImportedCollection Import(JsonObject root)
    {
        var resources = root.Arr("resources").Objects().ToList();
        var workspace = resources.FirstOrDefault(r => r.Str("_type") == "workspace");
        var workspaceId = workspace.Str("_id");

        var result = new ImportedCollection { Format = "Insomnia", Name = workspace.Str("name") ?? "Insomnia workspace" };

        // The base environment holds the variables requests use; the named ones override them.
        var environments = resources.Where(r => r.Str("_type") == "environment").ToList();
        var baseEnvironment = environments.FirstOrDefault(e => e.Str("parentId") == workspaceId) ?? environments.FirstOrDefault();
        var subEnvironments = environments.Where(e => !ReferenceEquals(e, baseEnvironment)).ToList();

        var variables = Flatten(baseEnvironment?.Obj("data"));
        var placeholders = new PlaceholderResolver(variables);

        var counts = new Counts();
        result.Items = BuildTree(resources, workspaceId, placeholders, counts);

        var known = variables
            .Where(v => IsHttpUrl(v.Value))
            .Select(v => new BaseUrlGrouper.KnownBase(v.Key, v.Value))
            .ToList();

        BaseUrlGrouper.Apply(result, known);

        AddSubEnvironmentUrls(result, subEnvironments, variables.Keys);

        if (placeholders.Describe() is { } text)
        {
            result.Warnings.Add(text);
        }

        if (counts.UnsupportedAuth.Count > 0)
        {
            foreach (var (type, count) in counts.UnsupportedAuth)
            {
                result.Warnings.Add($"{count} request{(count == 1 ? " uses" : "s use")} {type} authentication, which RESTTest does not support. " +
                                    "Add the credentials as a header instead.");
            }
        }

        if (counts.FileBodies > 0)
        {
            result.Warnings.Add($"{counts.FileBodies} request{(counts.FileBodies == 1 ? " sends" : "s send")} a binary file as the whole body, which RESTTest cannot do yet. The body was left empty.");
        }

        return result;
    }

    private sealed class Counts
    {
        public int FileBodies { get; set; }

        public Dictionary<string, int> UnsupportedAuth { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Insomnia's named environments (Staging, Production...) usually redefine the same URL variable.
    /// Each becomes a base URL of the collection, which is how RESTTest switches between them.
    /// </summary>
    private static void AddSubEnvironmentUrls(ImportedCollection result, List<JsonObject> subEnvironments, IEnumerable<string> baseVariableNames)
    {
        var names = baseVariableNames.ToHashSet(StringComparer.Ordinal);
        var used = result.Environments.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var environment in subEnvironments)
        {
            foreach (var (key, value) in Flatten(environment.Obj("data")))
            {
                if (!names.Contains(key) || !IsHttpUrl(value))
                {
                    continue;
                }

                var url = value.TrimEnd('/');
                if (result.Environments.Any(e => e.BaseUrl.Equals(url, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var name = environment.Str("name") ?? key;
                var candidate = name;
                for (var suffix = 2; !used.Add(candidate); suffix++)
                {
                    candidate = $"{name} {suffix}";
                }

                result.Environments.Add(new EnvironmentModel { Name = candidate, BaseUrl = url });
            }
        }
    }

    private static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static Dictionary<string, string> Flatten(JsonObject? data)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        if (data is not null)
        {
            foreach (var (key, node) in data)
            {
                if (node is JsonValue value)
                {
                    values[key] = value.TryGetValue<string>(out var text) ? text : value.ToJsonString();
                }
            }
        }

        return values;
    }

    private static List<NodeModel> BuildTree(List<JsonObject> resources, string? parentId, PlaceholderResolver placeholders, Counts counts)
    {
        // Insomnia orders siblings by a numeric sort key; the file's own order breaks ties.
        var children = resources
            .Select((r, index) => (Resource: r, Index: index))
            .Where(x => x.Resource.Str("parentId") == parentId && x.Resource.Str("_type") is "request_group" or "request")
            .OrderBy(x => double.TryParse(x.Resource.Str("metaSortKey"), System.Globalization.CultureInfo.InvariantCulture, out var key) ? key : 0)
            .ThenBy(x => x.Index)
            .Select(x => x.Resource);

        var nodes = new List<NodeModel>();

        foreach (var resource in children)
        {
            if (resource.Str("_type") == "request_group")
            {
                nodes.Add(new NodeModel
                {
                    Kind = NodeKind.Folder,
                    Name = resource.Str("name") ?? "Folder",
                    Children = BuildTree(resources, resource.Str("_id"), placeholders, counts),
                });
            }
            else
            {
                nodes.Add(new NodeModel
                {
                    Kind = NodeKind.Request,
                    Name = resource.Str("name") ?? "Request",
                    Request = ConvertRequest(resource, placeholders, counts),
                });
            }
        }

        return nodes;
    }

    private static RequestModel ConvertRequest(JsonObject source, PlaceholderResolver placeholders, Counts counts)
    {
        var request = new RequestModel
        {
            Method = (source.Str("method") ?? "GET").Trim().ToUpperInvariant(),
            BodyKind = BodyKind.None,
        };

        var url = placeholders.Resolve(source.Str("url"));
        var hash = url.IndexOf('#');
        if (hash >= 0)
        {
            url = url[..hash];
        }

        var question = url.IndexOf('?');
        if (question >= 0)
        {
            foreach (var pair in url[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var equals = pair.IndexOf('=');
                request.QueryParams.Add(new KeyValueModel
                {
                    Key = Decode(equals < 0 ? pair : pair[..equals]),
                    Value = Decode(equals < 0 ? string.Empty : pair[(equals + 1)..]),
                });
            }

            url = url[..question];
        }

        request.Path = url;

        foreach (var parameter in source.Arr("parameters").Objects())
        {
            if (parameter.Str("name") is { Length: > 0 } name)
            {
                request.QueryParams.Add(new KeyValueModel
                {
                    Enabled = !parameter.Flag("disabled"),
                    Key = name,
                    Value = placeholders.Resolve(parameter.Str("value")),
                });
            }
        }

        foreach (var header in source.Arr("headers").Objects())
        {
            if (header.Str("name") is { Length: > 0 } name)
            {
                request.Headers.Add(new KeyValueModel
                {
                    Enabled = !header.Flag("disabled"),
                    Key = name,
                    Value = placeholders.Resolve(header.Str("value")),
                });
            }
        }

        ApplyBody(source.Obj("body"), request, placeholders, counts);
        ApplyAuth(source.Obj("authentication"), request, placeholders, counts);
        return request;
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

    private static void ApplyBody(JsonObject? body, RequestModel request, PlaceholderResolver placeholders, Counts counts)
    {
        if (body is null)
        {
            return;
        }

        var mime = (body.Str("mimeType") ?? string.Empty).ToLowerInvariant();

        if (mime.StartsWith("application/x-www-form-urlencoded", StringComparison.Ordinal))
        {
            request.BodyKind = BodyKind.FormUrlEncoded;
            foreach (var field in body.Arr("params").Objects())
            {
                if (field.Str("name") is { Length: > 0 } name)
                {
                    request.FormFields.Add(new KeyValueModel
                    {
                        Enabled = !field.Flag("disabled"),
                        Key = name,
                        Value = placeholders.Resolve(field.Str("value")),
                    });
                }
            }

            return;
        }

        if (mime.StartsWith("multipart/form-data", StringComparison.Ordinal))
        {
            request.BodyKind = BodyKind.Multipart;
            foreach (var part in body.Arr("params").Objects())
            {
                if (part.Str("name") is not { Length: > 0 } name)
                {
                    continue;
                }

                var isFile = part.Str("type") == "file";
                request.MultipartParts.Add(new MultipartPartModel
                {
                    Enabled = !part.Flag("disabled"),
                    Key = name,
                    IsFile = isFile,
                    Value = isFile ? part.Str("fileName") ?? string.Empty : placeholders.Resolve(part.Str("value")),
                });
            }

            return;
        }

        if (mime.StartsWith("application/octet-stream", StringComparison.Ordinal))
        {
            counts.FileBodies++;
            return;
        }

        var text = placeholders.Resolve(body.Str("text"));
        if (mime.Length == 0 && text.Length == 0)
        {
            return;
        }

        request.Body = text;
        request.BodyKind = mime switch
        {
            _ when mime.Contains("json", StringComparison.Ordinal) || mime.Contains("graphql", StringComparison.Ordinal) => BodyKind.Json,
            _ when mime.Contains("xml", StringComparison.Ordinal) => BodyKind.Xml,
            _ => BodyKind.Text,
        };
    }

    private static void ApplyAuth(JsonObject? auth, RequestModel request, PlaceholderResolver placeholders, Counts counts)
    {
        var type = auth.Str("type")?.ToLowerInvariant();

        if (string.IsNullOrEmpty(type) || auth.Flag("disabled") || type == "none")
        {
            return;
        }

        switch (type)
        {
            case "bearer":
                request.Auth = AuthKind.BearerToken;
                request.BearerToken = placeholders.Resolve(auth.Str("token"));
                break;

            case "basic":
                request.Auth = AuthKind.Basic;
                request.BasicUsername = placeholders.Resolve(auth.Str("username"));
                request.BasicPassword = placeholders.Resolve(auth.Str("password"));
                break;

            case "apikey":
                request.Auth = AuthKind.ApiKey;
                request.ApiKeyName = placeholders.Resolve(auth.Str("key"));
                request.ApiKeyValue = placeholders.Resolve(auth.Str("value"));
                request.ApiKeyLocation = string.Equals(auth.Str("addTo"), "queryParams", StringComparison.OrdinalIgnoreCase)
                    ? ApiKeyLocation.Query
                    : ApiKeyLocation.Header;
                break;

            default:
                counts.UnsupportedAuth[type] = counts.UnsupportedAuth.GetValueOrDefault(type) + 1;
                break;
        }
    }
}
