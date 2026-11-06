using RESTTest.Models;

namespace RESTTest.Services.Importing;

/// <summary>
/// Other tools put the whole URL on every request; RESTTest keeps the host on the collection. This
/// finds the hosts, makes them the collection's base URLs, and shortens each request to a path.
/// </summary>
internal static class BaseUrlGrouper
{
    /// <summary>A base URL the source file names itself, e.g. Postman's <c>baseUrl</c> variable.</summary>
    public readonly record struct KnownBase(string Name, string Url);

    /// <param name="known">
    /// URLs the file defines under a name. A request whose URL starts with one is shortened against
    /// it (so a base like <c>https://api.x.com/v1</c> keeps its path), and the environment takes its name.
    /// </param>
    public static void Apply(ImportedCollection result, IReadOnlyList<KnownBase> known)
    {
        var requests = new List<RequestModel>();
        Collect(result.Items, requests);

        // base key -> (display name, URL, requests). Insertion order is first-seen order.
        var groups = new List<Group>();
        var assignments = new List<(RequestModel Request, Group Group, string Remainder)>();

        foreach (var request in requests)
        {
            var url = request.Path.Trim();

            if (!TrySplit(url, known, out var key, out var name, out var baseUrl, out var rest))
            {
                continue;
            }

            var group = groups.FirstOrDefault(g => g.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (group is null)
            {
                group = new Group(key, name, baseUrl);
                groups.Add(group);
            }

            group.Count++;
            assignments.Add((request, group, rest));
        }

        if (groups.Count == 0)
        {
            return;
        }

        // Most used first, ties by first appearance: that one is the active base URL.
        var ordered = groups
            .Select((g, index) => (Group: g, Index: index))
            .OrderByDescending(x => x.Group.Count)
            .ThenBy(x => x.Index)
            .Select(x => x.Group)
            .ToList();

        var active = ordered[0];

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in ordered)
        {
            result.Environments.Add(new EnvironmentModel { Name = Unique(group.Name, usedNames), BaseUrl = group.BaseUrl });
        }

        var elsewhere = 0;
        foreach (var (request, group, remainder) in assignments)
        {
            if (ReferenceEquals(group, active))
            {
                request.Path = remainder;
            }
            else
            {
                // Still a working request: an absolute path overrides the base URL when it is sent.
                elsewhere++;
            }
        }

        if (elsewhere > 0)
        {
            result.Warnings.Add(elsewhere == 1
                ? $"1 request uses a different host than \"{active.Name}\" and keeps its full URL in the path. Its host is listed under Manage URLs."
                : $"{elsewhere} requests use other hosts than \"{active.Name}\" and keep their full URL in the path. Their hosts are listed under Manage URLs.");
        }

        if (ordered.Any(g => g.BaseUrl.Length == 0))
        {
            var empty = ordered.Where(g => g.BaseUrl.Length == 0).Select(g => g.Name);
            result.Warnings.Add(
                $"No value was found for the base URL {string.Join(", ", empty.Select(n => "\"" + n + "\""))}. " +
                "Open Manage URLs and fill it in before sending.");
        }
    }

    private sealed class Group(string key, string name, string baseUrl)
    {
        public string Key { get; } = key;

        public string Name { get; } = name;

        public string BaseUrl { get; } = baseUrl;

        public int Count { get; set; }
    }

    /// <summary>
    /// Splits a request URL into (base, rest). False for anything that is neither an absolute
    /// http(s) URL nor starts with a placeholder: those are already relative paths and stay as they are.
    /// </summary>
    private static bool TrySplit(
        string url,
        IReadOnlyList<KnownBase> known,
        out string key,
        out string name,
        out string baseUrl,
        out string rest)
    {
        key = name = baseUrl = rest = string.Empty;

        var match = known
            .Where(k => k.Url.Length > 0 && StartsAtBoundary(url, k.Url))
            .OrderByDescending(k => k.Url.Length)
            .Cast<KnownBase?>()
            .FirstOrDefault();

        if (match is { } found)
        {
            var trimmed = found.Url.TrimEnd('/');
            key = trimmed;
            name = found.Name;
            baseUrl = trimmed;
            rest = url[trimmed.Length..];
            return true;
        }

        // An unresolved {{baseUrl}} prefix: remember it as an empty base URL for the user to fill in.
        if (url.StartsWith("{{", StringComparison.Ordinal) && url.IndexOf("}}", StringComparison.Ordinal) is var close and > 0)
        {
            var token = url[..(close + 2)];
            key = token;
            name = token.Trim('{', '}', ' ', '_', '.');
            baseUrl = string.Empty;
            rest = url[(close + 2)..];
            return true;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            var origin = uri.GetLeftPart(UriPartial.Authority);
            key = origin;
            name = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
            baseUrl = origin;
            rest = url[origin.Length..];
            return true;
        }

        return false;
    }

    private static bool StartsAtBoundary(string url, string prefix)
    {
        var trimmed = prefix.TrimEnd('/');
        return url.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)
               && (url.Length == trimmed.Length || url[trimmed.Length] is '/' or '?' or '#');
    }

    private static string Unique(string name, HashSet<string> used)
    {
        var candidate = string.IsNullOrWhiteSpace(name) ? "Server" : name.Trim();
        var suffix = 2;
        while (!used.Add(candidate))
        {
            candidate = $"{name.Trim()} {suffix++}";
        }

        return candidate;
    }

    private static void Collect(IEnumerable<NodeModel> nodes, List<RequestModel> into)
    {
        foreach (var node in nodes)
        {
            if (node is { Kind: NodeKind.Request, Request: { } request })
            {
                into.Add(request);
            }

            Collect(node.Children, into);
        }
    }
}
