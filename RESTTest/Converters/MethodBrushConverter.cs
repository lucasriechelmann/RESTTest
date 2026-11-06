using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RESTTest.Converters;

/// <summary>
/// Colours the method badge in the sidebar and the method combo box. The tones are chosen to
/// stay legible against both the light and dark Fluent backdrops.
/// </summary>
public sealed class MethodBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Get = Freeze("#3FA55A");
    private static readonly SolidColorBrush Post = Freeze("#D98A00");
    private static readonly SolidColorBrush Put = Freeze("#3B82D9");
    private static readonly SolidColorBrush Patch = Freeze("#9B59D0");
    private static readonly SolidColorBrush Delete = Freeze("#D9534F");
    private static readonly SolidColorBrush Other = Freeze("#8A8A8A");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string)?.ToUpperInvariant() switch
        {
            "GET" => Get,
            "POST" => Post,
            "PUT" => Put,
            "PATCH" or "PTCH" => Patch,
            "DELETE" or "DEL" => Delete,
            _ => Other,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        // Frozen brushes are shared safely and skip per-use cloning.
        brush.Freeze();
        return brush;
    }
}
