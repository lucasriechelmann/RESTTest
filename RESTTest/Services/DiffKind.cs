namespace RESTTest.Services;

public enum DiffKind
{
    Same,
    Added,
    Removed,

    /// <summary>A run of unchanged lines folded away; <see cref="DiffLine.Text"/> says how many.</summary>
    Gap,
}
