using System.Globalization;
using System.Windows.Data;
using RESTTest.Models;

namespace RESTTest.Converters;

/// <summary>Friendly labels for the assertion target and operator combo boxes.</summary>
public sealed class AssertionLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            AssertionTarget.StatusCode => "Status code",
            AssertionTarget.ResponseTimeMs => "Response time (ms)",
            AssertionTarget.Header => "Header",
            AssertionTarget.BodyText => "Body text",
            AssertionTarget.JsonPath => "JSON path",
            AssertionOperator.EqualTo => "equals",
            AssertionOperator.NotEqualTo => "does not equal",
            AssertionOperator.Contains => "contains",
            AssertionOperator.NotContains => "does not contain",
            AssertionOperator.Exists => "exists",
            AssertionOperator.LessThan => "is less than",
            AssertionOperator.GreaterThan => "is greater than",
            _ => value?.ToString() ?? string.Empty,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
