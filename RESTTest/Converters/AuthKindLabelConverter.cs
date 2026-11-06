using System.Globalization;
using System.Windows.Data;
using RESTTest.Models;

namespace RESTTest.Converters;

/// <summary>Friendly labels for the authentication combo box.</summary>
public sealed class AuthKindLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is AuthKind kind
            ? kind switch
            {
                AuthKind.None => "No Authentication",
                AuthKind.BearerToken => "Bearer Token",
                _ => kind.ToString(),
            }
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
