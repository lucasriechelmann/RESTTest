namespace RESTTest.ViewModels;

/// <summary>Where a dragged node lands relative to the node it was dropped on.</summary>
public enum DropPlacement
{
    /// <summary>Insert as a sibling above the target.</summary>
    Before,

    /// <summary>Insert as a sibling below the target.</summary>
    After,

    /// <summary>Append inside the target folder.</summary>
    Inside,
}
