namespace RESTTest.Services;

/// <summary>
/// Line-by-line comparison of two texts (Myers' O(ND) algorithm, the one behind <c>git diff</c>),
/// used to show how a response changed between two sends. Pure and synchronous.
/// </summary>
public static class TextDiff
{
    /// <summary>Texts longer than this many lines are not compared: the answer would be unreadable anyway.</summary>
    public const int MaxLines = 20_000;

    /// <summary>
    /// Beyond this many edits Myers' memory grows with the square of the edit count, so a text that
    /// differs this much is shown as "all removed, all added" instead of a precise diff.
    /// </summary>
    private const int MaxEdits = 2_000;

    public static DiffResult Compare(string? before, string? after)
    {
        var a = SplitLines(before);
        var b = SplitLines(after);

        if (a.Length > MaxLines || b.Length > MaxLines)
        {
            return new DiffResult { Lines = [], TooLarge = true };
        }

        // Most diffs are a few changed values in a long, equal document: peel off what both share.
        var start = 0;
        while (start < a.Length && start < b.Length && a[start] == b[start])
        {
            start++;
        }

        var endA = a.Length;
        var endB = b.Length;
        while (endA > start && endB > start && a[endA - 1] == b[endB - 1])
        {
            endA--;
            endB--;
        }

        var middle = Myers(a[start..endA], b[start..endB]);

        var lines = new List<DiffLine>(a.Length + b.Length);
        var oldNumber = 1;
        var newNumber = 1;

        for (var i = 0; i < start; i++)
        {
            lines.Add(new DiffLine(DiffKind.Same, a[i], oldNumber++, newNumber++));
        }

        var added = 0;
        var removed = 0;

        foreach (var (kind, text) in middle)
        {
            switch (kind)
            {
                case DiffKind.Same:
                    lines.Add(new DiffLine(DiffKind.Same, text, oldNumber++, newNumber++));
                    break;
                case DiffKind.Removed:
                    lines.Add(new DiffLine(DiffKind.Removed, text, oldNumber++, null));
                    removed++;
                    break;
                default:
                    lines.Add(new DiffLine(DiffKind.Added, text, null, newNumber++));
                    added++;
                    break;
            }
        }

        for (var i = endA; i < a.Length; i++)
        {
            lines.Add(new DiffLine(DiffKind.Same, a[i], oldNumber++, newNumber++));
        }

        return new DiffResult { Lines = lines, Added = added, Removed = removed };
    }

    /// <summary>Lines of the text without their line endings. Null and empty both mean "no lines".</summary>
    private static string[] SplitLines(string? text) =>
        string.IsNullOrEmpty(text)
            ? []
            : text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n');

    private static List<(DiffKind Kind, string Text)> Myers(string[] a, string[] b)
    {
        var result = new List<(DiffKind, string)>();

        if (a.Length == 0 || b.Length == 0)
        {
            result.AddRange(a.Select(line => (DiffKind.Removed, line)));
            result.AddRange(b.Select(line => (DiffKind.Added, line)));
            return result;
        }

        var n = a.Length;
        var m = b.Length;
        var max = Math.Min(n + m, MaxEdits);
        var offset = max + 1;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();
        var finished = false;

        for (var d = 0; d <= max && !finished; d++)
        {
            trace.Add((int[])v.Clone());

            for (var k = -d; k <= d; k += 2)
            {
                int x;
                if (k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1]))
                {
                    x = v[offset + k + 1];
                }
                else
                {
                    x = v[offset + k - 1] + 1;
                }

                var y = x - k;
                while (x < n && y < m && a[x] == b[y])
                {
                    x++;
                    y++;
                }

                v[offset + k] = x;

                if (x >= n && y >= m)
                {
                    finished = true;
                    break;
                }
            }
        }

        if (!finished)
        {
            // Too different to trace precisely: say so plainly instead of guessing.
            result.AddRange(a.Select(line => (DiffKind.Removed, line)));
            result.AddRange(b.Select(line => (DiffKind.Added, line)));
            return result;
        }

        // Walk the trace backwards from the end, emitting edits in reverse.
        var px = n;
        var py = m;

        for (var d = trace.Count - 1; d >= 0; d--)
        {
            var snapshot = trace[d];
            var k = px - py;

            int previousK;
            if (k == -d || (k != d && snapshot[offset + k - 1] < snapshot[offset + k + 1]))
            {
                previousK = k + 1;
            }
            else
            {
                previousK = k - 1;
            }

            var previousX = snapshot[offset + previousK];
            var previousY = previousX - previousK;

            while (px > previousX && py > previousY)
            {
                result.Add((DiffKind.Same, a[px - 1]));
                px--;
                py--;
            }

            if (d > 0)
            {
                result.Add(px == previousX
                    ? (DiffKind.Added, b[previousY])
                    : (DiffKind.Removed, a[previousX]));
            }

            px = previousX;
            py = previousY;
        }

        result.Reverse();
        return result;
    }
}
