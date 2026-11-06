namespace RESTTest.Services;

/// <summary>One line of a diff, with its line number in the old text, the new text, or both.</summary>
public sealed record DiffLine(DiffKind Kind, string Text, int? OldNumber, int? NewNumber)
{
    /// <summary>"+", "-" or an empty string: the marker column of a unified diff.</summary>
    public string Marker => Kind switch
    {
        DiffKind.Added => "+",
        DiffKind.Removed => "-",
        _ => string.Empty,
    };

    public string OldNumberText => OldNumber?.ToString() ?? string.Empty;

    public string NewNumberText => NewNumber?.ToString() ?? string.Empty;
}
