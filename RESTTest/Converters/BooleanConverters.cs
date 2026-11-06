using System.Globalization;
using System.Windows;
using System.Windows.Data;
using RESTTest.Models;

namespace RESTTest.Converters;

/// <summary>Collapses when the bound boolean is true (the inverse of the built-in converter).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

/// <summary>Shows an element only when the bound string has content.</summary>
public sealed class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>True when the bound <see cref="AuthKind"/> equals the one named in the parameter.</summary>
public sealed class AuthKindMatchConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is AuthKind kind
        && parameter is string expected
        && Enum.TryParse<AuthKind>(expected, out var match)
        && kind == match;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
