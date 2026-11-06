namespace RESTTest.Services;

/// <summary>A matched pair of brackets, as offsets into the document.</summary>
public readonly record struct BracketMatch(int OpenOffset, int CloseOffset);

/// <summary>
/// Finds the bracket pair surrounding the caret.
/// <para>
/// Deliberately a pure function over a string rather than something tied to the editor, so the
/// tricky part — ignoring brackets that live inside JSON string literals, such as the brace in
/// <c>{"a": "}"}</c> — can be tested on its own.
/// </para>
/// </summary>
public static class JsonBracketMatcher
{
    /// <summary>
    /// Documents above this size skip matching. The scan is linear and runs on every caret
    /// move, which is free for a request body but not for a multi-megabyte response.
    /// </summary>
    public const int MaxDocumentLength = 512 * 1024;

    /// <summary>
    /// Looks for a bracket immediately before or at <paramref name="caretOffset"/> and resolves
    /// its partner. Returns false when the caret is not on a bracket, the bracket is unmatched,
    /// or it sits inside a string.
    /// </summary>
    public static bool TryFindMatch(string? text, int caretOffset, out BracketMatch match)
    {
        match = default;

        if (string.IsNullOrEmpty(text)
            || text.Length > MaxDocumentLength
            || caretOffset < 0
            || caretOffset > text.Length)
        {
            return false;
        }

        // Prefer the character just before the caret, which is where it sits after typing a
        // closing brace; fall back to the one under it.
        int candidate;
        if (caretOffset > 0 && IsBracket(text[caretOffset - 1]))
        {
            candidate = caretOffset - 1;
        }
        else if (caretOffset < text.Length && IsBracket(text[caretOffset]))
        {
            candidate = caretOffset;
        }
        else
        {
            return false;
        }

        return TryResolve(text, candidate, out match);
    }

    private static bool TryResolve(string text, int candidate, out BracketMatch match)
    {
        match = default;

        var open = new Stack<int>();
        var inString = false;
        var escaped = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inString)
            {
                // Inside a literal nothing is structural; \" must not end the string.
                if (escaped)
                {
                    escaped = false;
                }
                else if (c == '\\')
                {
                    escaped = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c is '{' or '[')
            {
                open.Push(i);
                continue;
            }

            if (c is not ('}' or ']') || open.Count == 0)
            {
                continue;
            }

            var opener = open.Pop();

            // Mismatched pairs (a '[' closed by '}') are malformed; ignore rather than guess.
            if (!IsPair(text[opener], c))
            {
                continue;
            }

            if (opener == candidate || i == candidate)
            {
                match = new BracketMatch(opener, i);
                return true;
            }
        }

        return false;
    }

    private static bool IsBracket(char c) => c is '{' or '}' or '[' or ']';

    private static bool IsPair(char opener, char closer) =>
        (opener == '{' && closer == '}') || (opener == '[' && closer == ']');
}
