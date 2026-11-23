using RESTTest.Models;
using RESTTest.Services;

namespace RESTTest.ViewModels;

/// <summary>
/// A stored capture value, shown in the Captures tab. Immutable: a new capture of the same name
/// replaces the row, and <see cref="CollectionViewModel"/> marks itself dirty when the list changes.
/// </summary>
public sealed class CapturedValueViewModel(CapturedValueModel model)
{
    public string Name { get; } = model.Name;

    public string Value { get; } = model.Value;

    public string Source { get; } = model.Source;

    public DateTimeOffset CapturedAt { get; } = model.CapturedAt;

    /// <summary>One line: the value, trimmed for display. The tooltip shows it whole.</summary>
    public string ValuePreview => CaptureEvaluator.Preview(Value);

    public string Detail => string.IsNullOrWhiteSpace(Source)
        ? CapturedAt.ToLocalTime().ToString("g")
        : $"from {Source} · {CapturedAt.ToLocalTime():g}";

    public CapturedValueModel ToModel() => new()
    {
        Name = Name,
        Value = Value,
        Source = Source,
        CapturedAt = CapturedAt,
    };
}
