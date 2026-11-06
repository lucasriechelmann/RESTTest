using CommunityToolkit.Mvvm.ComponentModel;
using RESTTest.Models;

namespace RESTTest.ViewModels;

/// <summary>
/// One editable row in a request's Tests tab. Only the five persisted fields are saved; the
/// option lists and visibility flags derive from <see cref="Target"/> and are transient.
/// </summary>
public partial class AssertionViewModel : EditableViewModel
{
    public static IReadOnlyList<AssertionTarget> Targets { get; } =
        [
            AssertionTarget.StatusCode,
            AssertionTarget.ResponseTimeMs,
            AssertionTarget.Header,
            AssertionTarget.JsonPath,
            AssertionTarget.BodyText,
        ];

    private static readonly AssertionOperator[] NumericOperators =
        [AssertionOperator.EqualTo, AssertionOperator.NotEqualTo, AssertionOperator.LessThan, AssertionOperator.GreaterThan];

    private static readonly AssertionOperator[] TextOperators =
        [AssertionOperator.EqualTo, AssertionOperator.NotEqualTo, AssertionOperator.Contains, AssertionOperator.NotContains];

    private static readonly AssertionOperator[] LookupOperators =
        [
            AssertionOperator.Exists,
            AssertionOperator.EqualTo,
            AssertionOperator.NotEqualTo,
            AssertionOperator.Contains,
            AssertionOperator.NotContains,
            AssertionOperator.LessThan,
            AssertionOperator.GreaterThan,
        ];

    [ObservableProperty]
    private bool _enabled = true;

    [ObservableProperty]
    private AssertionTarget _target = AssertionTarget.StatusCode;

    /// <summary>
    /// The header name or JSON path. Deliberately not called "Property": a field named
    /// <c>_property</c> makes the MVVM toolkit generate a hook <c>OnPropertyChanged(string)</c>,
    /// which shadows <c>ObservableObject.OnPropertyChanged(string)</c> and silently turns every
    /// <c>OnPropertyChanged(nameof(...))</c> call in this class into a no-op.
    /// </summary>
    [ObservableProperty]
    private string _selector = string.Empty;

    [ObservableProperty]
    private AssertionOperator _operator = AssertionOperator.EqualTo;

    [ObservableProperty]
    private string _expected = "200";

    public AssertionViewModel()
    {
    }

    public AssertionViewModel(AssertionModel model)
    {
        _enabled = model.Enabled;
        _target = model.Target;
        _selector = model.Selector;
        _operator = model.Operator;
        _expected = model.Expected;
    }

    /// <summary>Instance view of the static list, so XAML can bind it without x:Static.</summary>
    public IReadOnlyList<AssertionTarget> TargetOptions => Targets;

    /// <summary>Only the comparisons that make sense for the current target.</summary>
    public IReadOnlyList<AssertionOperator> OperatorOptions => OperatorsFor(Target);

    /// <summary>Header and JSON path targets need a name or path; the others do not.</summary>
    public bool ShowsSelector => Target is AssertionTarget.Header or AssertionTarget.JsonPath;

    /// <summary>"Exists" compares nothing, so the expected-value box is hidden for it.</summary>
    public bool ShowsExpected => Operator != AssertionOperator.Exists;

    public string SelectorPlaceholder => Target == AssertionTarget.Header ? "Content-Type" : "$.data[0].id";

    public static IReadOnlyList<AssertionOperator> OperatorsFor(AssertionTarget target) => target switch
    {
        AssertionTarget.StatusCode or AssertionTarget.ResponseTimeMs => NumericOperators,
        AssertionTarget.BodyText => TextOperators,
        _ => LookupOperators,
    };

    public AssertionModel ToModel() => new()
    {
        Enabled = Enabled,
        Target = Target,
        Selector = Selector,
        Operator = Operator,
        Expected = Expected,
    };

    protected override bool IsTransient(string? propertyName) =>
        propertyName is nameof(OperatorOptions)
            or nameof(ShowsSelector)
            or nameof(ShowsExpected)
            or nameof(SelectorPlaceholder);

    partial void OnTargetChanged(AssertionTarget value)
    {
        // Fix the operator before announcing the new option list. Otherwise the combo box sees a
        // selected item that is not in its new list and writes null back into the binding.
        var allowed = OperatorsFor(value);
        if (!allowed.Contains(Operator))
        {
            Operator = allowed[0];
        }

        OnPropertyChanged(nameof(OperatorOptions));
        OnPropertyChanged(nameof(ShowsSelector));
        OnPropertyChanged(nameof(SelectorPlaceholder));
    }

    partial void OnOperatorChanged(AssertionOperator value) => OnPropertyChanged(nameof(ShowsExpected));
}
