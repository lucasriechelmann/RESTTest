namespace RESTTest.Services;

/// <summary>The outcome of comparing two texts line by line.</summary>
public sealed class DiffResult
{
    /// <summary>Every line of both texts in order: unchanged, removed and added.</summary>
    public required IReadOnlyList<DiffLine> Lines { get; init; }

    public int Added { get; init; }

    public int Removed { get; init; }

    /// <summary>True when the texts were too long to compare; <see cref="Lines"/> is then empty.</summary>
    public bool TooLarge { get; init; }

    public bool IsIdentical => !TooLarge && Added == 0 && Removed == 0;

    /// <summary>"+3 -1" or "No differences".</summary>
    public string Summary =>
        TooLarge ? "Too large to compare"
        : IsIdentical ? "No differences"
        : $"+{Added} -{Removed}";

    /// <summary>
    /// Only the changed lines with <paramref name="context"/> unchanged lines around them; each longer
    /// run of unchanged lines becomes a single <see cref="DiffKind.Gap"/> row, as in a pull request.
    /// </summary>
    public IReadOnlyList<DiffLine> WithContext(int context = 3)
    {
        if (IsIdentical || Lines.Count == 0)
        {
            return Lines;
        }

        var keep = new bool[Lines.Count];

        for (var i = 0; i < Lines.Count; i++)
        {
            if (Lines[i].Kind == DiffKind.Same)
            {
                continue;
            }

            for (var j = Math.Max(0, i - context); j <= Math.Min(Lines.Count - 1, i + context); j++)
            {
                keep[j] = true;
            }
        }

        var folded = new List<DiffLine>();
        var skipped = 0;

        for (var i = 0; i < Lines.Count; i++)
        {
            if (keep[i])
            {
                if (skipped > 0)
                {
                    folded.Add(Gap(skipped));
                    skipped = 0;
                }

                folded.Add(Lines[i]);
            }
            else
            {
                skipped++;
            }
        }

        if (skipped > 0)
        {
            folded.Add(Gap(skipped));
        }

        return folded;
    }

    private static DiffLine Gap(int count) =>
        new(DiffKind.Gap, count == 1 ? "1 unchanged line" : $"{count} unchanged lines", null, null);
}
