using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RESTTest.Services;

/// <summary>
/// Runs a JSONPath query against a JSON body and returns every value it selects, to filter a
/// response down to the part that matters. Supports the parts of JSONPath people actually type:
/// <c>$</c>, <c>.name</c>, <c>['name']</c>, <c>[0]</c> and <c>[-1]</c>, <c>.*</c> and <c>[*]</c>,
/// recursive descent <c>..name</c>, slices <c>[1:3]</c> and <c>[::2]</c>, unions <c>[0,2]</c> or
/// <c>['a','b']</c>, and filters <c>[?(@.price &lt; 10 &amp;&amp; @.tags)]</c> with
/// <c>== != &lt; &lt;= &gt; &gt;= =~</c>, <c>&amp;&amp; || !</c>, parentheses and <c>length(...)</c>.
/// </summary>
/// <remarks>
/// <see cref="JsonPathLite"/> is the single-value lookup assertions use; this is the many-value
/// query. The leading <c>$</c> is optional, as it is there.
/// </remarks>
public static class JsonPathQuery
{
    /// <summary>More matches than this are dropped, so a careless <c>$..*</c> on a big body stays responsive.</summary>
    public const int MaxMatches = 5_000;

    private const int MaxWorkingSet = 200_000;

    private static readonly JsonDocumentOptions ReaderOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public static JsonPathResult Run(string? json, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Fail("Type a path such as $.items[0].id");
        }

        Query query;
        try
        {
            query = new PathParser(path).ParseQuery();
        }
        catch (PathSyntaxException ex)
        {
            return Fail(ex.Message);
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return Fail("There is no body to search.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, ReaderOptions);
        }
        catch (JsonException)
        {
            return Fail("The body is not JSON, so a path cannot be applied to it.");
        }

        using (document)
        {
            var root = document.RootElement;
            var current = new List<Node> { new("$", root) };
            var truncated = false;

            foreach (var segment in query.Segments)
            {
                var next = new List<Node>();

                foreach (var node in current)
                {
                    if (segment.Descendant)
                    {
                        foreach (var inner in SelfAndDescendants(node))
                        {
                            Apply(segment, inner, root, next);
                        }
                    }
                    else
                    {
                        Apply(segment, node, root, next);
                    }

                    if (next.Count > MaxWorkingSet)
                    {
                        truncated = true;
                        break;
                    }
                }

                current = next;
            }

            if (current.Count > MaxMatches)
            {
                truncated = true;
                current.RemoveRange(MaxMatches, current.Count - MaxMatches);
            }

            return new JsonPathResult
            {
                Ok = true,
                IsDefinite = query.IsDefinite,
                Truncated = truncated,
                Matches = [.. current.Select(n => new JsonPathMatch(n.Path, n.Value.GetRawText()))],
            };
        }
    }

    private static JsonPathResult Fail(string message) => new() { Ok = false, Error = message };

    // ---- Evaluation ----------------------------------------------------------------------------

    private readonly record struct Node(string Path, JsonElement Value);

    private static void Apply(Segment segment, Node node, JsonElement root, List<Node> into)
    {
        foreach (var selector in segment.Selectors)
        {
            switch (selector)
            {
                case NameSelector name:
                    if (node.Value.ValueKind == JsonValueKind.Object && node.Value.TryGetProperty(name.Name, out var property))
                    {
                        into.Add(new Node(node.Path + "[" + Quote(name.Name) + "]", property));
                    }

                    break;

                case IndexSelector index:
                    if (node.Value.ValueKind == JsonValueKind.Array)
                    {
                        var length = node.Value.GetArrayLength();
                        var i = index.Index < 0 ? index.Index + length : index.Index;
                        if (i >= 0 && i < length)
                        {
                            into.Add(new Node($"{node.Path}[{i}]", node.Value[i]));
                        }
                    }

                    break;

                case WildcardSelector:
                    into.AddRange(Children(node));
                    break;

                case SliceSelector slice:
                    if (node.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var i in slice.Indexes(node.Value.GetArrayLength()))
                        {
                            into.Add(new Node($"{node.Path}[{i}]", node.Value[i]));
                        }
                    }

                    break;

                case FilterSelector filter:
                    foreach (var child in Children(node))
                    {
                        if (filter.Test(child.Value, root))
                        {
                            into.Add(child);
                        }
                    }

                    break;
            }
        }
    }

    private static IEnumerable<Node> Children(Node node)
    {
        switch (node.Value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in node.Value.EnumerateObject())
                {
                    yield return new Node(node.Path + "[" + Quote(property.Name) + "]", property.Value);
                }

                break;

            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in node.Value.EnumerateArray())
                {
                    yield return new Node($"{node.Path}[{i++}]", item);
                }

                break;
        }
    }

    /// <summary>The node, then everything below it, parents before children (document order).</summary>
    private static IEnumerable<Node> SelfAndDescendants(Node node)
    {
        yield return node;

        foreach (var child in Children(node))
        {
            foreach (var inner in SelfAndDescendants(child))
            {
                yield return inner;
            }
        }
    }

    private static string Quote(string name) => "'" + name.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

    // ---- Syntax tree ---------------------------------------------------------------------------

    private sealed record Query(List<Segment> Segments)
    {
        /// <summary>Only names and indexes, one selector each, no recursion: at most one result.</summary>
        public bool IsDefinite => Segments.All(s => !s.Descendant && s.Selectors.Count == 1 && s.Selectors[0] is NameSelector or IndexSelector);
    }

    private sealed record Segment(bool Descendant, List<Selector> Selectors);

    private abstract record Selector;

    private sealed record NameSelector(string Name) : Selector;

    private sealed record IndexSelector(int Index) : Selector;

    private sealed record WildcardSelector : Selector;

    private sealed record FilterSelector(Func<JsonElement, JsonElement, bool> Test) : Selector;

    /// <summary>[start:end:step] with the usual defaults and negative-index handling.</summary>
    private sealed record SliceSelector(int? Start, int? End, int Step) : Selector
    {
        public IEnumerable<int> Indexes(int length)
        {
            if (Step == 0)
            {
                yield break;
            }

            int Normalise(int value) => value < 0 ? value + length : value;

            if (Step > 0)
            {
                var lower = Math.Clamp(Normalise(Start ?? 0), 0, length);
                var upper = Math.Clamp(Normalise(End ?? length), 0, length);
                for (var i = lower; i < upper; i += Step)
                {
                    yield return i;
                }
            }
            else
            {
                var upper = Math.Clamp(Normalise(Start ?? length - 1), -1, length - 1);
                var lower = Math.Clamp(Normalise(End ?? -length - 1), -1, length - 1);
                for (var i = upper; i > lower; i += Step)
                {
                    yield return i;
                }
            }
        }
    }

    private sealed class PathSyntaxException(string message) : Exception(message);

    // ---- Parsing -------------------------------------------------------------------------------

    /// <summary>An operand in a filter: a literal, a path into the current item or the root, or <c>length(...)</c>.</summary>
    private sealed record Operand(Func<JsonElement, JsonElement, (bool Exists, JsonElement Value)> Eval, bool IsPath, string? StringLiteral = null);

    private sealed class PathParser(string text)
    {
        private int _pos;

        private bool End => _pos >= text.Length;

        private char Peek => text[_pos];

        public Query ParseQuery()
        {
            SkipSpaces();
            var hadDollar = false;

            if (!End && Peek == '$')
            {
                hadDollar = true;
                _pos++;
            }

            var segments = new List<Segment>();

            while (true)
            {
                SkipSpaces();
                if (End)
                {
                    break;
                }

                var c = Peek;

                if (c == '.')
                {
                    if (_pos + 1 < text.Length && text[_pos + 1] == '.')
                    {
                        _pos += 2;
                        segments.Add(ParseAfterDot(descendant: true));
                    }
                    else
                    {
                        _pos++;
                        segments.Add(ParseAfterDot(descendant: false));
                    }
                }
                else if (c == '[')
                {
                    segments.Add(new Segment(false, ParseBracket()));
                }
                else if (segments.Count == 0 && !hadDollar)
                {
                    // "items[0].id" with no leading $ or dot: the first name stands on its own.
                    segments.Add(new Segment(false, [new NameSelector(ParseName())]));
                }
                else
                {
                    throw Error($"Unexpected '{c}' at position {_pos + 1}. Use a dot or brackets between steps, as in $.items[0].id");
                }
            }

            return new Query(segments);
        }

        private Segment ParseAfterDot(bool descendant)
        {
            if (End)
            {
                throw Error($"A name is missing after the dot at position {_pos}.");
            }

            if (Peek == '[')
            {
                return new Segment(descendant, ParseBracket());
            }

            if (Peek == '*')
            {
                _pos++;
                return new Segment(descendant, [new WildcardSelector()]);
            }

            return new Segment(descendant, [new NameSelector(ParseName())]);
        }

        private string ParseName()
        {
            var start = _pos;
            while (!End && Peek != '.' && Peek != '[')
            {
                _pos++;
            }

            var name = text[start.._pos].Trim();
            if (name.Length == 0)
            {
                throw Error($"A name is missing at position {start + 1}.");
            }

            return name;
        }

        private List<Selector> ParseBracket()
        {
            var open = _pos;
            _pos++; // [
            var selectors = new List<Selector>();

            while (true)
            {
                SkipSpaces();
                if (End)
                {
                    throw Error($"The '[' at position {open + 1} is never closed with ']'.");
                }

                selectors.Add(ParseSelector());
                SkipSpaces();

                if (End)
                {
                    throw Error($"The '[' at position {open + 1} is never closed with ']'.");
                }

                if (Peek == ',')
                {
                    _pos++;
                    continue;
                }

                if (Peek == ']')
                {
                    _pos++;
                    return selectors;
                }

                throw Error($"Expected ',' or ']' at position {_pos + 1}, found '{Peek}'.");
            }
        }

        private Selector ParseSelector()
        {
            var c = Peek;

            if (c == '*')
            {
                _pos++;
                return new WildcardSelector();
            }

            if (c is '\'' or '"')
            {
                return new NameSelector(ParseQuoted());
            }

            if (c == '?')
            {
                _pos++;
                return new FilterSelector(ParseOr());
            }

            if (char.IsDigit(c) || c is '-' or ':')
            {
                var start = _pos;
                while (!End && (char.IsDigit(Peek) || Peek is '-' or ':' or ' '))
                {
                    _pos++;
                }

                var token = text[start.._pos].Replace(" ", string.Empty);

                if (token.Contains(':'))
                {
                    var parts = token.Split(':');
                    if (parts.Length > 3 || !TryOptionalInt(parts[0], out var from) || !TryOptionalInt(parts[1], out var to)
                        || (parts.Length == 3 && !TryOptionalInt(parts[2], out _)))
                    {
                        throw Error($"The slice '[{token}]' at position {start + 1} is not valid. Use [start:end] or [start:end:step].");
                    }

                    var step = parts.Length == 3 && parts[2].Length > 0 ? int.Parse(parts[2], CultureInfo.InvariantCulture) : 1;
                    return new SliceSelector(from, to, step);
                }

                if (!int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var index))
                {
                    throw Error($"'{token}' at position {start + 1} is not an array index.");
                }

                return new IndexSelector(index);
            }

            throw Error($"Unexpected '{c}' at position {_pos + 1}. Inside [ ] use an index, 'a name', *, a slice or ?(filter).");
        }

        private static bool TryOptionalInt(string token, out int? value)
        {
            value = null;
            if (token.Length == 0)
            {
                return true;
            }

            if (int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
                return true;
            }

            return false;
        }

        private string ParseQuoted()
        {
            var quote = Peek;
            var start = _pos;
            _pos++;
            var builder = new System.Text.StringBuilder();

            while (!End && Peek != quote)
            {
                if (Peek == '\\' && _pos + 1 < text.Length)
                {
                    _pos++;
                }

                builder.Append(Peek);
                _pos++;
            }

            if (End)
            {
                throw Error($"The quote at position {start + 1} is never closed.");
            }

            _pos++;
            return builder.ToString();
        }

        // ---- Filters: or > and > not > comparison ----------------------------------------------

        private Func<JsonElement, JsonElement, bool> ParseOr()
        {
            var left = ParseAnd();

            while (Match("||"))
            {
                var l = left;
                var right = ParseAnd();
                left = (current, root) => l(current, root) || right(current, root);
            }

            return left;
        }

        private Func<JsonElement, JsonElement, bool> ParseAnd()
        {
            var left = ParseUnary();

            while (Match("&&"))
            {
                var l = left;
                var right = ParseUnary();
                left = (current, root) => l(current, root) && right(current, root);
            }

            return left;
        }

        private Func<JsonElement, JsonElement, bool> ParseUnary()
        {
            SkipSpaces();

            if (!End && Peek == '!' && !(_pos + 1 < text.Length && text[_pos + 1] == '='))
            {
                _pos++;
                var inner = ParseUnary();
                return (current, root) => !inner(current, root);
            }

            if (!End && Peek == '(')
            {
                var open = _pos;
                _pos++;
                var inner = ParseOr();
                SkipSpaces();

                if (End || Peek != ')')
                {
                    throw Error($"The '(' at position {open + 1} is never closed with ')'.");
                }

                _pos++;
                return inner;
            }

            return ParseComparison();
        }

        private Func<JsonElement, JsonElement, bool> ParseComparison()
        {
            var left = ParseOperand();
            SkipSpaces();

            var op = ReadOperator();
            if (op is null)
            {
                if (!left.IsPath)
                {
                    throw Error($"A filter needs a comparison near position {_pos + 1}, for example @.price > 10.");
                }

                // A bare path means "this item has it".
                return (current, root) => left.Eval(current, root).Exists;
            }

            SkipSpaces();
            var right = ParseOperand();

            if (op == "=~")
            {
                if (right.StringLiteral is null)
                {
                    throw Error("After =~ put a quoted regular expression, for example @.name =~ 'ada.*'.");
                }

                Regex regex;
                try
                {
                    regex = new Regex(right.StringLiteral, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
                }
                catch (ArgumentException ex)
                {
                    throw Error($"The regular expression is not valid: {ex.Message}");
                }

                return (current, root) =>
                {
                    var (exists, value) = left.Eval(current, root);
                    if (!exists || value.ValueKind != JsonValueKind.String)
                    {
                        return false;
                    }

                    try
                    {
                        return regex.IsMatch(value.GetString() ?? string.Empty);
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        return false;
                    }
                };
            }

            return (current, root) => Compare(op, left.Eval(current, root), right.Eval(current, root));
        }

        private string? ReadOperator()
        {
            foreach (var op in new[] { "==", "!=", "<=", ">=", "=~", "<", ">" })
            {
                if (string.CompareOrdinal(text, _pos, op, 0, op.Length) == 0)
                {
                    _pos += op.Length;
                    return op;
                }
            }

            return null;
        }

        private Operand ParseOperand()
        {
            SkipSpaces();

            if (End)
            {
                throw Error("The filter ends too early: a value is missing.");
            }

            var c = Peek;

            if (c is '@' or '$')
            {
                _pos++;
                var steps = ParseSingularSteps();
                var fromRoot = c == '$';

                return new Operand((current, root) => Walk(fromRoot ? root : current, steps), IsPath: true);
            }

            if (c is '\'' or '"')
            {
                var value = ParseQuoted();
                var element = ToElement(System.Text.Json.JsonSerializer.Serialize(value));
                return new Operand((_, _) => (true, element), IsPath: false, StringLiteral: value);
            }

            if (char.IsDigit(c) || c == '-')
            {
                var start = _pos;
                _pos++;
                while (!End && (char.IsDigit(Peek) || Peek is '.' or 'e' or 'E' or '+' or '-'))
                {
                    _pos++;
                }

                var token = text[start.._pos];
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    throw Error($"'{token}' at position {start + 1} is not a number.");
                }

                var element = ToElement(token);
                return new Operand((_, _) => (true, element), IsPath: false);
            }

            var wordStart = _pos;
            while (!End && (char.IsLetter(Peek) || Peek == '_'))
            {
                _pos++;
            }

            var word = text[wordStart.._pos];

            switch (word)
            {
                case "true" or "false" or "null":
                    var literal = ToElement(word);
                    return new Operand((_, _) => (true, literal), IsPath: false);

                case "length" when !End && Peek == '(':
                    _pos++;
                    var inner = ParseOperand();
                    SkipSpaces();
                    if (End || Peek != ')')
                    {
                        throw Error($"length( at position {wordStart + 1} is never closed with ')'.");
                    }

                    _pos++;
                    return new Operand((current, root) => Length(inner.Eval(current, root)), IsPath: false);
            }

            throw Error(word.Length == 0
                ? $"Unexpected '{c}' at position {wordStart + 1} in the filter."
                : $"'{word}' at position {wordStart + 1} is not a value. Quote text ('{word}'), or start a path with @ or $.");
        }

        private List<object> ParseSingularSteps()
        {
            var steps = new List<object>();

            while (!End)
            {
                if (Peek == '.')
                {
                    _pos++;
                    var start = _pos;
                    while (!End && (char.IsLetterOrDigit(Peek) || Peek is '_' or '-' or '$'))
                    {
                        _pos++;
                    }

                    if (_pos == start)
                    {
                        throw Error($"A name is missing after the dot at position {start}.");
                    }

                    steps.Add(text[start.._pos]);
                }
                else if (Peek == '[')
                {
                    _pos++;
                    SkipSpaces();

                    if (!End && Peek is '\'' or '"')
                    {
                        steps.Add(ParseQuoted());
                    }
                    else
                    {
                        var start = _pos;
                        while (!End && (char.IsDigit(Peek) || Peek == '-'))
                        {
                            _pos++;
                        }

                        if (!int.TryParse(text[start.._pos], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var index))
                        {
                            throw Error($"A name or index is expected inside [ ] at position {start + 1} of the filter.");
                        }

                        steps.Add(index);
                    }

                    SkipSpaces();
                    if (End || Peek != ']')
                    {
                        throw Error($"A ']' is missing at position {_pos + 1}.");
                    }

                    _pos++;
                }
                else
                {
                    break;
                }
            }

            return steps;
        }

        private bool Match(string token)
        {
            SkipSpaces();

            if (string.CompareOrdinal(text, _pos, token, 0, token.Length) == 0)
            {
                _pos += token.Length;
                return true;
            }

            return false;
        }

        private void SkipSpaces()
        {
            while (!End && char.IsWhiteSpace(Peek))
            {
                _pos++;
            }
        }

        private static PathSyntaxException Error(string message) => new(message);
    }

    // ---- Filter values -------------------------------------------------------------------------

    private static JsonElement ToElement(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static (bool Exists, JsonElement Value) Walk(JsonElement start, List<object> steps)
    {
        var current = start;

        foreach (var step in steps)
        {
            switch (step)
            {
                case string name when current.ValueKind == JsonValueKind.Object && current.TryGetProperty(name, out var property):
                    current = property;
                    break;

                case int index when current.ValueKind == JsonValueKind.Array:
                    var length = current.GetArrayLength();
                    var i = index < 0 ? index + length : index;
                    if (i < 0 || i >= length)
                    {
                        return (false, default);
                    }

                    current = current[i];
                    break;

                default:
                    return (false, default);
            }
        }

        return (true, current);
    }

    private static (bool Exists, JsonElement Value) Length((bool Exists, JsonElement Value) operand)
    {
        if (!operand.Exists)
        {
            return (false, default);
        }

        int? length = operand.Value.ValueKind switch
        {
            JsonValueKind.String => operand.Value.GetString()?.Length,
            JsonValueKind.Array => operand.Value.GetArrayLength(),
            JsonValueKind.Object => operand.Value.EnumerateObject().Count(),
            _ => null,
        };

        return length is { } n ? (true, ToElement(n.ToString(CultureInfo.InvariantCulture))) : (false, default);
    }

    private static bool Compare(string op, (bool Exists, JsonElement Value) a, (bool Exists, JsonElement Value) b)
    {
        // A missing value equals only another missing value; it is never smaller or larger than anything.
        if (!a.Exists || !b.Exists)
        {
            var bothMissing = !a.Exists && !b.Exists;
            return op switch
            {
                "==" => bothMissing,
                "!=" => !bothMissing,
                _ => false,
            };
        }

        var kindA = a.Value.ValueKind;
        var kindB = b.Value.ValueKind;

        // true and false are different kinds in System.Text.Json but one "boolean" type here.
        bool sameType = kindA == kindB
                        || (kindA is JsonValueKind.True or JsonValueKind.False && kindB is JsonValueKind.True or JsonValueKind.False);

        int? order = null;
        bool? equal = null;

        if (sameType)
        {
            switch (kindA)
            {
                case JsonValueKind.Number:
                    order = a.Value.GetDouble().CompareTo(b.Value.GetDouble());
                    equal = order == 0;
                    break;

                case JsonValueKind.String:
                    order = string.CompareOrdinal(a.Value.GetString(), b.Value.GetString());
                    equal = order == 0;
                    break;

                case JsonValueKind.True or JsonValueKind.False:
                    equal = kindA == kindB;
                    break;

                case JsonValueKind.Null:
                    equal = true;
                    break;

                default:
                    equal = a.Value.GetRawText() == b.Value.GetRawText();
                    break;
            }
        }

        return op switch
        {
            "==" => equal == true,
            "!=" => equal != true,
            "<" => order < 0,
            "<=" => order <= 0,
            ">" => order > 0,
            ">=" => order >= 0,
            _ => false,
        };
    }
}
