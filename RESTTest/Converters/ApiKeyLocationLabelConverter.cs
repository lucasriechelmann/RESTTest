using System.Globalization;
using System.Windows.Data;
using RESTTest.Models;

namespace RESTTest.Converters;

/// <summary>Friendly labels for the "Add to" combo box on the API key form.</summary>
public sealed class ApiKeyLocationLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ApiKeyLocation location
            ? location switch
            {
                ApiKeyLocation.Header => "Header",
                ApiKeyLocation.Query => "Query parameter",
                _ => location.ToString(),
            }
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
