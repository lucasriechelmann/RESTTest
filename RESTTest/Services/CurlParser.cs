using System.Globalization;
using System.Net;
using System.Text;
using RESTTest.Models;

namespace RESTTest.Services;

/// <summary>The outcome of reading a cURL command: the request, plus anything the user should know.</summary>
public sealed class CurlImport
{
    public required RequestModel Request { get; init; }

    /// <summary>A short name for the new request, e.g. "POST /users".</summary>
    public required string SuggestedName { get; init; }

    /// <summary>
    /// True when the URL's host did not match the active base URL, so the whole URL was kept in the
    /// request's path (which the sender accepts as an override) instead of a relative path.
    /// </summary>
    public bool KeptAbsoluteUrl { get; init; }

    /// <summary>Things in the command that were skipped or guessed, in plain language.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Reads a cURL command line (as copied from browser dev tools, API docs or a terminal) into a
/// <see cref="RequestModel"/>. Pure text in, model out: no I/O, so every case can be unit tested.
/// </summary>
/// <remarks>
/// Supports the options people actually paste: method, headers, the data options, forms and file
/// uploads, basic and bearer auth, cookies, user agent, referer, and <c>--json</c>. Anything else
/// is ignored, and the ones that change meaning (output files, proxies, files read as data) are
/// reported in <see cref="CurlImport.Warnings"/>. POSIX quoting only; a Windows cmd command (with
/// <c>^</c> escapes) is detected and refused with a pointer to the bash flavour.
/// </remarks>
public static class CurlParser
{
    /// <summary>Short options that take a value. Anything else in a cluster like -sSL is a plain flag.</summary>
    private const string ShortOptionsWithValue = "XHdubAeFomxwTKEtYyzrCcDPQU";

    private static readonly Dictionary<char, string> ShortToLong = new()
    {
        ['X'] = "request", ['H'] = "header", ['d'] = "data", ['u'] = "user", ['b'] = "cookie",
        ['A'] = "user-agent", ['e'] = "referer", ['F'] = "form", ['I'] = "head", ['G'] = "get",
        ['k'] = "insecure", ['o'] = "output", ['x'] = "proxy", ['T'] = "upload-file",
        ['m'] = "max-time", ['w'] = "write-out", ['K'] = "config", ['E'] = "cert",
    };

    /// <summary>Long options that take a value; they must consume it or it would be mistaken for the URL.</summary>
    private static readonly HashSet<string> LongOptionsWithValue = new(StringComparer.Ordinal)
    {
        "request", "header", "data", "data-raw", "data-binary", "data-ascii", "data-urlencode",
        "form", "form-string", "user", "cookie", "user-agent", "referer", "url", "json",
        "output", "max-time", "connect-timeout", "proxy", "write-out", "upload-file", "cacert",
        "cert", "key", "config", "resolve", "retry", "retry-delay", "retry-max-time", "limit-rate",
        "dump-header", "cookie-jar", "range", "interface", "proxy-user", "capath", "cert-type",
        "key-type", "pass", "oauth2-bearer", "time-cond", "continue-at", "speed-limit", "speed-time",
        "ftp-port", "quote", "telnet-option", "proxy-header", "request-target", "keepalive-time",
        "max-redirs", "max-filesize", "local-port", "dns-servers", "doh-url", "expect100-timeout",
    };

    /// <summary>Flags that change nothing about the request a tester cares about; ignored without comment.</summary>
    private static readonly HashSet<string> QuietFlags = new(StringComparer.Ordinal)
    {
        "compressed", "silent", "show-error", "verbose", "include", "location", "globoff", "fail",
        "no-buffer", "http1.0", "http1.1", "http2", "http2-prior-knowledge", "http3", "progress-bar",
        "location-trusted", "tcp-nodelay", "no-keepalive", "ipv4", "ipv6", "remote-name",
        "remote-header-name", "junk-session-cookies", "raw", "styled-output", "fail-with-body",
        "trace-time", "no-progress-meter", "tr-encoding", "path-as-is", "ssl-reqd", "tlsv1.2", "tlsv1.3",
        "disable", "manual", "help", "version", "anyauth", "basic", "digest", "ntlm", "negotiate",
    };

    private static readonly Dictionary<char, string> ShortFlagToLong = new()
    {
        ['s'] = "silent", ['S'] = "show-error", ['v'] = "verbose", ['i'] = "include", ['L'] = "location",
        ['g'] = "globoff", ['f'] = "fail", ['N'] = "no-buffer", ['#'] = "progress-bar", ['O'] = "remote-name",
        ['J'] = "remote-header-name", ['4'] = "ipv4", ['6'] = "ipv6", ['q'] = "disable",
    };

    public static bool TryParse(string? command, string? baseUrl, out CurlImport? result, out string? error)
    {
        result = null;
        error = null;

        if (string.IsNullOrWhiteSpace(command))
        {
            error = "Paste a cURL command to import.";
            return false;
        }

        // Chrome's "Copy as cURL (cmd)" escapes quotes with ^ and cannot be read with POSIX rules.
        if (command.Contains("^\"") || command.Contains(" ^\n") || command.Contains(" ^\r\n"))
        {
            error = "This looks like a Windows cmd cURL command (it uses ^ escapes). " +
                    "Copy it again as \"cURL (bash)\" and paste that.";
            return false;
        }

        command = NormaliseCommandName(command);

        if (!TryTokenize(command, out var tokens, out error))
        {
            return false;
        }

        // Prompt characters people copy along with the command.
        while (tokens.Count > 0 && tokens[0] is "$" or ">" or "%")
        {
            tokens.RemoveAt(0);
        }

        if (tokens.Count == 0 || !IsCurlName(tokens[0]))
        {
            error = "That does not look like a cURL command. It should start with \"curl\".";
            return false;
        }

        return new Reader(tokens, baseUrl).Run(out result, out error);
    }

    /// <summary>
    /// "C:\Windows\System32\curl.exe ..." would lose its backslashes to shell escaping before the name
    /// could be checked, so a path that ends in curl is cut down to plain "curl".
    /// </summary>
    private static string NormaliseCommandName(string command)
    {
        var text = command.TrimStart();
        var end = 0;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        var word = text[..end];
        if (!word.Contains('\\'))
        {
            return command;
        }

        var name = word[(word.LastIndexOf('\\') + 1)..];
        return name.Equals("curl", StringComparison.OrdinalIgnoreCase) || name.Equals("curl.exe", StringComparison.OrdinalIgnoreCase)
            ? "curl" + text[end..]
            : command;
    }

    private static bool IsCurlName(string token)
    {
        var name = token.Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];
        return name.Equals("curl", StringComparison.OrdinalIgnoreCase)
               || name.Equals("curl.exe", StringComparison.OrdinalIgnoreCase);
    }

    // ---- Tokenizer ------------------------------------------------------------------------

    /// <summary>
    /// Splits a command the way a POSIX shell would: whitespace separates words; single quotes are
    /// literal; double quotes allow a few backslash escapes; <c>$'...'</c> understands \n, \xHH and
    /// \uHHHH (browsers use it for bodies with newlines); a backslash before a newline joins lines.
    /// </summary>
    private static bool TryTokenize(string text, out List<string> tokens, out string? error)
    {
        // A local, not the out parameter: the local function below cannot capture an out parameter.
        var words = new List<string>();
        tokens = words;
        error = null;

        var current = new StringBuilder();
        var inWord = false;
        var i = 0;

        void EndWord()
        {
            if (inWord)
            {
                words.Add(current.ToString());
                current.Clear();
                inWord = false;
            }
        }

        while (i < text.Length)
        {
            var c = text[i];

            if (char.IsWhiteSpace(c))
            {
                EndWord();
                i++;
                continue;
            }

            switch (c)
            {
                case '\\':
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i += 2;
                    }
                    else if (i + 2 < text.Length && text[i + 1] == '\r' && text[i + 2] == '\n')
                    {
                        i += 3;
                    }
                    else if (i + 1 < text.Length)
                    {
                        current.Append(text[i + 1]);
                        inWord = true;
                        i += 2;
                    }
                    else
                    {
                        i++;
                    }

                    break;

                case '\'':
                {
                    var end = text.IndexOf('\'', i + 1);
                    if (end < 0)
                    {
                        error = "A single quote (') was opened but never closed.";
                        return false;
                    }

                    current.Append(text, i + 1, end - i - 1);
                    inWord = true;
                    i = end + 1;
                    break;
                }

                case '"':
                    if (!ReadDoubleQuoted(text, ref i, current))
                    {
                        error = "A double quote (\") was opened but never closed.";
                        return false;
                    }

                    inWord = true;
                    break;

                case '$' when i + 1 < text.Length && text[i + 1] == '\'':
                    if (!ReadAnsiC(text, ref i, current))
                    {
                        error = "A $'...' quote was opened but never closed.";
                        return false;
                    }

                    inWord = true;
                    break;

                default:
                    current.Append(c);
                    inWord = true;
                    i++;
                    break;
            }
        }

        EndWord();
        return true;
    }

    private static bool ReadDoubleQuoted(string text, ref int i, StringBuilder into)
    {
        i++;

        while (i < text.Length)
        {
            var c = text[i];

            if (c == '"')
            {
                i++;
                return true;
            }

            if (c == '\\' && i + 1 < text.Length)
            {
                var next = text[i + 1];

                if (next is '"' or '\\' or '$' or '`')
                {
                    into.Append(next);
                    i += 2;
                    continue;
                }

                if (next == '\n')
                {
                    i += 2;
                    continue;
                }
            }

            into.Append(c);
            i++;
        }

        return false;
    }

    private static bool ReadAnsiC(string text, ref int i, StringBuilder into)
    {
        i += 2;

        while (i < text.Length)
        {
            var c = text[i];

            if (c == '\'')
            {
                i++;
                return true;
            }

            if (c != '\\' || i + 1 >= text.Length)
            {
                into.Append(c);
                i++;
                continue;
            }

            var next = text[i + 1];
            i += 2;

            switch (next)
            {
                case 'n': into.Append('\n'); break;
                case 't': into.Append('\t'); break;
                case 'r': into.Append('\r'); break;
                case 'a': into.Append('\a'); break;
                case 'b': into.Append('\b'); break;
                case 'e': into.Append('\u001b'); break;
                case 'f': into.Append('\f'); break;
                case 'v': into.Append('\v'); break;
                case '\\': into.Append('\\'); break;
                case '\'': into.Append('\''); break;
                case '"': into.Append('"'); break;
                case '?': into.Append('?'); break;
                case 'x': into.Append(ReadHex(text, ref i, 2)); break;
                case 'u': into.Append(ReadHex(text, ref i, 4)); break;
                case 'U': into.Append(ReadHex(text, ref i, 8)); break;
                case >= '0' and <= '7':
                {
                    var value = next - '0';
                    for (var digits = 1; digits < 3 && i < text.Length && text[i] is >= '0' and <= '7'; digits++, i++)
                    {
                        value = (value * 8) + (text[i] - '0');
                    }

                    into.Append((char)value);
                    break;
                }

                default:
                    into.Append('\\').Append(next);
                    break;
            }
        }

        return false;
    }

    private static string ReadHex(string text, ref int i, int maxDigits)
    {
        var start = i;
        while (i < text.Length && i - start < maxDigits && Uri.IsHexDigit(text[i]))
        {
            i++;
        }

        if (i == start)
        {
            return string.Empty;
        }

        var code = int.Parse(text.AsSpan(start, i - start), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return char.ConvertFromUtf32(code is > 0x10FFFF or (>= 0xD800 and <= 0xDFFF) ? 0xFFFD : code);
    }

    // ---- Reading the options --------------------------------------------------------------

    private sealed class Reader(List<string> tokens, string? baseUrl)
    {
        private readonly List<string> _warnings = [];
        private readonly List<KeyValueModel> _headers = [];
        private readonly List<string> _urls = [];

        /// <summary>Data from -d / --data-raw / --data-binary / --json, in order.</summary>
        private readonly List<string> _rawData = [];

        /// <summary>--data-urlencode values, in order.</summary>
        private readonly List<string> _encodedData = [];

        private readonly List<MultipartPartModel> _parts = [];

        private string? _method;
        private bool _head;
        private bool _getMode;
        private AuthKind _auth = AuthKind.None;
        private string _bearerToken = string.Empty;
        private bool _json;

        /// <summary>
        /// True once any data or form option was seen, even an empty one or one that was skipped
        /// (a file read). curl sends a POST for all of them, so the method follows this.
        /// </summary>
        private bool _sawPayload;

        public bool Run(out CurlImport? result, out string? error)
        {
            result = null;
            error = null;

            if (!ReadOptions(out error))
            {
                return false;
            }

            if (_urls.Count == 0)
            {
                error = "No URL found in the command.";
                return false;
            }

            if (_urls.Count > 1)
            {
                _warnings.Add("The command has more than one URL; only the first was imported.");
            }

            if (!TryBuildUrl(_urls[0], out var path, out var query, out var keptAbsolute, out var displayUrl, out error))
            {
                return false;
            }

            var request = new RequestModel { Path = path, QueryParams = query };

            ApplyAuthAndHeaders(request);
            ApplyBody(request);

            request.Method = _method
                             ?? (_head ? "HEAD"
                                 : _getMode ? "GET"
                                 : _sawPayload ? "POST" : "GET");

            result = new CurlImport
            {
                Request = request,
                SuggestedName = SuggestName(request.Method, keptAbsolute ? displayUrl : path),
                KeptAbsoluteUrl = keptAbsolute,
                Warnings = _warnings,
            };
            return true;
        }

        private bool ReadOptions(out string? error)
        {
            // A local rather than the out parameter, which a local function cannot capture.
            string? failure = null;
            error = null;
            var i = 1; // tokens[0] is "curl"
            var optionsEnded = false;

            string? NextValue(string option)
            {
                if (i >= tokens.Count)
                {
                    failure = $"The option {option} needs a value, but the command ends after it.";
                    return null;
                }

                return tokens[i++];
            }


            while (i < tokens.Count)
            {
                var token = tokens[i++];

                if (optionsEnded || token == "-" || !token.StartsWith('-'))
                {
                    _urls.Add(token);
                    continue;
                }

                if (token == "--")
                {
                    optionsEnded = true;
                    continue;
                }

                if (token.StartsWith("--", StringComparison.Ordinal))
                {
                    var name = token[2..];
                    string? value = null;

                    // Not curl syntax, but --header=value shows up in docs and is unambiguous.
                    var equals = name.IndexOf('=');
                    if (equals > 0 && LongOptionsWithValue.Contains(name[..equals]))
                    {
                        value = name[(equals + 1)..];
                        name = name[..equals];
                    }

                    if (LongOptionsWithValue.Contains(name))
                    {
                        value ??= NextValue(token);
                        if (value is null)
                        {
                            error = failure;
                            return false;
                        }
                    }

                    Apply(name, value);
                    continue;
                }

                // Single dash: one option, or a cluster such as -sSL, or an attached value such as -XPOST.
                for (var j = 1; j < token.Length; j++)
                {
                    var ch = token[j];

                    if (ShortOptionsWithValue.Contains(ch))
                    {
                        var value = j + 1 < token.Length ? token[(j + 1)..] : NextValue("-" + ch);
                        if (value is null)
                        {
                            error = failure;
                            return false;
                        }

                        Apply(ShortToLong.GetValueOrDefault(ch, ch.ToString()), value);
                        break;
                    }

                    Apply(
                        ShortToLong.TryGetValue(ch, out var longName) ? longName
                        : ShortFlagToLong.GetValueOrDefault(ch, ch.ToString()),
                        null);
                }
            }

            return true;
        }

        private void Apply(string name, string? value)
        {
            switch (name)
            {
                case "request":
                    _method = value!.Trim().ToUpperInvariant();
                    break;

                case "header":
                    AddHeader(value!);
                    break;

                case "data":
                case "data-ascii":
                case "data-binary":
                    _sawPayload = true;

                    if (value!.StartsWith('@'))
                    {
                        _warnings.Add($"Skipped \"{name} {value}\": reading a body from a file is not supported. " +
                                      "Use the Form (multipart) type to upload a file.");
                    }
                    else
                    {
                        _rawData.Add(value);
                    }

                    break;

                case "data-raw":
                    _sawPayload = true;
                    _rawData.Add(value!);
                    break;

                case "json":
                    _sawPayload = true;
                    _rawData.Add(value!);
                    _json = true;
                    break;

                case "data-urlencode":
                    _sawPayload = true;
                    _encodedData.Add(value!);
                    break;

                case "form":
                    _sawPayload = true;
                    AddFormPart(value!, literal: false);
                    break;

                case "form-string":
                    _sawPayload = true;
                    AddFormPart(value!, literal: true);
                    break;

                case "user":
                    AddBasicAuth(value!);
                    break;

                case "oauth2-bearer":
                    _auth = AuthKind.BearerToken;
                    _bearerToken = value!.Trim();
                    break;

                case "cookie":
                    if (value!.Contains('='))
                    {
                        _headers.Add(new KeyValueModel { Key = "Cookie", Value = value });
                    }
                    else
                    {
                        _warnings.Add($"Skipped \"--cookie {value}\": reading cookies from a file is not supported.");
                    }

                    break;

                case "user-agent":
                    _headers.Add(new KeyValueModel { Key = "User-Agent", Value = value! });
                    break;

                case "referer":
                    _headers.Add(new KeyValueModel { Key = "Referer", Value = value! });
                    break;

                case "url":
                    _urls.Add(value!);
                    break;

                case "head":
                    _head = true;
                    break;

                case "get":
                    _getMode = true;
                    break;

                case "insecure":
                    _warnings.Add("-k/--insecure was ignored: certificate errors are only tolerated for localhost.");
                    break;

                case "output":
                case "remote-name":
                case "dump-header":
                    _warnings.Add("Saving the response to a file (-o/-O/-D) is not supported; the response is shown in the app.");
                    break;

                case "proxy":
                case "proxy-user":
                    _warnings.Add("The proxy option was ignored: requests are sent directly.");
                    break;

                case "upload-file":
                    _warnings.Add("-T/--upload-file was ignored. Use the Form (multipart) type to upload a file.");
                    break;

                default:
                    var isKnownShortWithValue = name.Length == 1 && ShortOptionsWithValue.Contains(name[0]);

                    if (!QuietFlags.Contains(name) && !LongOptionsWithValue.Contains(name) && !isKnownShortWithValue)
                    {
                        _warnings.Add($"Ignored an unrecognised option: {(name.Length == 1 ? "-" : "--")}{name}");
                    }

                    break;
            }
        }

        private void AddHeader(string raw)
        {
            var colon = raw.IndexOf(':');

            if (colon <= 0)
            {
                // "Name;" sends an empty header and "Name:" removes one; neither has a value to keep.
                if (!raw.TrimEnd().EndsWith(';') && !raw.TrimEnd().EndsWith(':'))
                {
                    _warnings.Add($"Skipped a header without a colon: {raw}");
                }

                return;
            }

            var key = raw[..colon].Trim();
            var value = raw[(colon + 1)..].Trim();

            if (value.Length == 0)
            {
                return;
            }

            _headers.Add(new KeyValueModel { Key = key, Value = value });
        }

        private void AddBasicAuth(string credentials)
        {
            if (!credentials.Contains(':'))
            {
                _warnings.Add("-u without a password would prompt for one; the password was left empty.");
                credentials += ":";
            }

            // Sent as a plain header: the app has no basic-auth type, and this is what it puts on the wire.
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
            _headers.Add(new KeyValueModel { Key = "Authorization", Value = $"Basic {encoded}" });
        }

        private void AddFormPart(string raw, bool literal)
        {
            var equals = raw.IndexOf('=');
            if (equals <= 0)
            {
                _warnings.Add($"Skipped a form field without a name: {raw}");
                return;
            }

            var key = raw[..equals];
            var value = raw[(equals + 1)..];

            if (!literal && value.StartsWith('@'))
            {
                _parts.Add(new MultipartPartModel { Key = key, Value = ReadFilePath(value[1..]), IsFile = true });
                return;
            }

            if (!literal && value.StartsWith('<'))
            {
                _warnings.Add($"Skipped form field \"{key}\": reading its value from a file (<) is not supported.");
                return;
            }

            if (!literal)
            {
                // "value;type=text/plain" carries a content type the app does not model per field.
                var typed = value.LastIndexOf(";type=", StringComparison.OrdinalIgnoreCase);
                if (typed >= 0 && value.IndexOf(';', typed + 1) < 0)
                {
                    value = value[..typed];
                }
            }

            _parts.Add(new MultipartPartModel { Key = key, Value = value });
        }

        /// <summary>The file part of -F "name=@path;type=...", with optional quotes around a path containing ';'.</summary>
        private static string ReadFilePath(string spec)
        {
            if (spec.StartsWith('"'))
            {
                var builder = new StringBuilder();
                for (var i = 1; i < spec.Length; i++)
                {
                    if (spec[i] == '\\' && i + 1 < spec.Length && spec[i + 1] is '"' or '\\')
                    {
                        builder.Append(spec[++i]);
                    }
                    else if (spec[i] == '"')
                    {
                        break;
                    }
                    else
                    {
                        builder.Append(spec[i]);
                    }
                }

                return builder.ToString();
            }

            var semicolon = spec.IndexOf(';');
            return semicolon < 0 ? spec : spec[..semicolon];
        }

        private bool TryBuildUrl(
            string raw,
            out string path,
            out List<KeyValueModel> query,
            out bool keptAbsolute,
            out string displayUrl,
            out string? error)
        {
            path = string.Empty;
            query = [];
            keptAbsolute = false;
            displayUrl = string.Empty;
            error = null;

            var url = raw.Trim();

            var hash = url.IndexOf('#');
            if (hash >= 0)
            {
                url = url[..hash];
            }

            var queryText = string.Empty;
            var question = url.IndexOf('?');
            if (question >= 0)
            {
                queryText = url[(question + 1)..];
                url = url[..question];
            }

            query = ParsePairs(queryText);

            // A bare path: nothing to compare with the base URL, so keep it relative.
            if (url.StartsWith('/'))
            {
                path = url;
                displayUrl = url;
                return true;
            }

            // curl assumes http:// when no scheme is given.
            if (!url.Contains("://", StringComparison.Ordinal))
            {
                url = "http://" + url;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                error = $"\"{raw}\" is not a valid http or https URL.";
                return false;
            }

            displayUrl = $"{uri.Authority}{(uri.AbsolutePath == "/" && !url.EndsWith('/') ? string.Empty : uri.AbsolutePath)}";

            var root = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (root.Length > 0
                && url.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && (url.Length == root.Length || url[root.Length] == '/'))
            {
                path = url[root.Length..];
                return true;
            }

            path = url;
            keptAbsolute = true;
            return true;
        }

        private void ApplyAuthAndHeaders(RequestModel request)
        {
            request.Auth = _auth;
            request.BearerToken = _bearerToken;

            foreach (var header in _headers)
            {
                if (request.Auth == AuthKind.None
                    && header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                    && header.Value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    request.Auth = AuthKind.BearerToken;
                    request.BearerToken = header.Value["Bearer ".Length..].Trim();
                    continue;
                }

                request.Headers.Add(header);
            }

            if (_json && !request.Headers.Any(h => h.Key.Equals("Accept", StringComparison.OrdinalIgnoreCase)))
            {
                request.Headers.Add(new KeyValueModel { Key = "Accept", Value = "application/json" });
            }
        }

        private static bool HasBody(RequestModel request) => request.BodyKind switch
        {
            BodyKind.FormUrlEncoded => request.FormFields.Count > 0,
            BodyKind.Multipart => request.MultipartParts.Count > 0,
            BodyKind.None => false,
            _ => !string.IsNullOrWhiteSpace(request.Body),
        };

        private void ApplyBody(RequestModel request)
        {
            // -F wins over -d, as in curl (the two cannot be combined).
            if (_parts.Count > 0)
            {
                request.BodyKind = BodyKind.Multipart;
                request.MultipartParts = _parts;
                RemoveHeader(request, "Content-Type", m => m.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase));
                return;
            }

            if (_rawData.Count == 0 && _encodedData.Count == 0)
            {
                request.BodyKind = BodyKind.None;
                return;
            }

            // -G turns the data into query parameters of a GET.
            if (_getMode)
            {
                foreach (var pair in ParsePairs(string.Join('&', _rawData)))
                {
                    request.QueryParams.Add(pair);
                }

                request.QueryParams.AddRange(EncodedFields());
                request.BodyKind = BodyKind.None;
                return;
            }

            var contentType = FindHeader(request, "Content-Type");
            var mediaType = contentType?.Value.Split(';')[0].Trim().ToLowerInvariant();

            if (_encodedData.Count > 0)
            {
                request.BodyKind = BodyKind.FormUrlEncoded;
                request.FormFields = [.. ParsePairs(string.Join('&', _rawData)), .. EncodedFields()];
                RemoveHeader(request, "Content-Type", m => m == "application/x-www-form-urlencoded");
                return;
            }

            var body = string.Join('&', _rawData);

            if (body.Length == 0)
            {
                request.BodyKind = BodyKind.None;
                return;
            }

            if (_json || mediaType?.Contains("json") == true)
            {
                request.BodyKind = BodyKind.Json;
                request.Body = body;
                RemoveHeader(request, "Content-Type", m => m == "application/json");
            }
            else if (mediaType?.Contains("xml") == true)
            {
                request.BodyKind = BodyKind.Xml;
                request.Body = body;
                RemoveHeader(request, "Content-Type", m => m == "application/xml");
            }
            else if (mediaType == "text/plain")
            {
                request.BodyKind = BodyKind.Text;
                request.Body = body;
                RemoveHeader(request, "Content-Type", m => m == "text/plain");
            }
            else if (mediaType is null && LooksLikeJson(body))
            {
                // curl would send this as a form, but nobody pastes JSON meaning that.
                request.BodyKind = BodyKind.Json;
                request.Body = body;
                _warnings.Add("The command had no Content-Type; the body looks like JSON, so it was imported as JSON.");
            }
            else if (mediaType is null or "application/x-www-form-urlencoded")
            {
                request.BodyKind = BodyKind.FormUrlEncoded;
                request.FormFields = ParsePairs(body);
                RemoveHeader(request, "Content-Type", m => m == "application/x-www-form-urlencoded");
            }
            else
            {
                // Some other type (csv, octet-stream...): send the text, and keep the header to say so.
                request.BodyKind = BodyKind.Text;
                request.Body = body;
            }
        }

        private List<KeyValueModel> EncodedFields()
        {
            var fields = new List<KeyValueModel>();

            foreach (var raw in _encodedData)
            {
                var at = raw.IndexOf('@');
                var equals = raw.IndexOf('=');

                // name@file reads a file; "name=content" and "=content" carry the content itself.
                if (at > 0 && (equals < 0 || at < equals))
                {
                    _warnings.Add($"Skipped \"--data-urlencode {raw}\": reading a value from a file is not supported.");
                    continue;
                }

                if (equals <= 0)
                {
                    _warnings.Add($"Skipped a --data-urlencode value without a name: {raw}");
                    continue;
                }

                // curl sends the name as written, so one containing %20 is already encoded; this app
                // encodes names itself when sending, so it is decoded here to avoid encoding it twice.
                fields.Add(new KeyValueModel { Key = WebUtility.UrlDecode(raw[..equals]), Value = raw[(equals + 1)..] });
            }

            return fields;
        }

        private static KeyValueModel? FindHeader(RequestModel request, string name) =>
            request.Headers.LastOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Drops a header the body type already implies, so the request does not say it twice.</summary>
        private static void RemoveHeader(RequestModel request, string name, Func<string, bool> isRedundant)
        {
            var header = FindHeader(request, name);
            if (header is not null && isRedundant(header.Value.Split(';')[0].Trim().ToLowerInvariant()))
            {
                request.Headers.Remove(header);
            }
        }

        private static bool LooksLikeJson(string body)
        {
            var trimmed = body.TrimStart();
            return (trimmed.StartsWith('{') || trimmed.StartsWith('[')) && JsonText.IsValid(body);
        }

        private static string SuggestName(string method, string where)
        {
            var label = string.IsNullOrWhiteSpace(where) ? "/" : where;
            return $"{method} {label}";
        }
    }

    /// <summary>Splits "a=1&amp;b=2" into rows, decoding %XX and + the way a server would.</summary>
    private static List<KeyValueModel> ParsePairs(string text)
    {
        var rows = new List<KeyValueModel>();

        foreach (var pair in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var key = equals < 0 ? pair : pair[..equals];
            var value = equals < 0 ? string.Empty : pair[(equals + 1)..];

            rows.Add(new KeyValueModel { Key = WebUtility.UrlDecode(key), Value = WebUtility.UrlDecode(value) });
        }

        return rows;
    }
}
