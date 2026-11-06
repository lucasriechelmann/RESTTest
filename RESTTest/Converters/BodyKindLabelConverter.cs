using System.Globalization;
using System.Windows.Data;
using RESTTest.Models;

namespace RESTTest.Converters;

/// <summary>Friendly labels for the body type combo box.</summary>
public sealed class BodyKindLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is BodyKind kind
            ? kind switch
            {
                BodyKind.Json => "JSON",
                BodyKind.Text => "Text",
                BodyKind.Xml => "XML",
                BodyKind.FormUrlEncoded => "Form (urlencoded)",
                BodyKind.Multipart => "Form (multipart, files)",
                BodyKind.None => "No body",
                _ => kind.ToString(),
            }
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
