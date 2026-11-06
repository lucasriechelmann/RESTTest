using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace RESTTest.Converters;

/// <summary>Colours the response status pill from <c>ResponseViewModel.StatusCategory</c>.</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Success = Freeze("#3FA55A");
    private static readonly SolidColorBrush Redirect = Freeze("#3B82D9");
    private static readonly SolidColorBrush ClientError = Freeze("#D98A00");
    private static readonly SolidColorBrush ServerError = Freeze("#D9534F");
    private static readonly SolidColorBrush Unknown = Freeze("#8A8A8A");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value as string) switch
        {
            "Success" => Success,
            "Redirect" => Redirect,
            "ClientError" => ClientError,
            "ServerError" => ServerError,
            _ => Unknown,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
