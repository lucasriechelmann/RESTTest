using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace RESTTest.Services;

/// <summary>Pretty-printing and validation for the XML body, the counterpart of <see cref="JsonText"/>.</summary>
public static class XmlText
{
    /// <summary>
    /// Re-indents <paramref name="input"/> with two spaces. Returns false and a reason (with the
    /// line and position) when the text is not well-formed XML, leaving the caller's text alone.
    /// </summary>
    public static bool TryFormat(string? input, out string formatted, out string? error)
        => TryWrite(input, SaveOptions.None, out formatted, out error);

    /// <summary>Collapses the XML onto one line, dropping the whitespace between elements.</summary>
    public static bool TryMinify(string? input, out string minified, out string? error)
        => TryWrite(input, SaveOptions.DisableFormatting, out minified, out error);

    private static bool TryWrite(string? input, SaveOptions options, out string output, out string? error)
    {
        output = input ?? string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "There is nothing to format.";
            return false;
        }

        try
        {
            // Whitespace-only text between elements is layout, not content, so it is dropped on
            // load and re-created (or not) on save. Without this, formatting would stack up blanks.
            var document = XDocument.Parse(input, LoadOptions.None);
            var settings = new XmlWriterSettings
            {
                Indent = options != SaveOptions.DisableFormatting,
                IndentChars = "  ",
                OmitXmlDeclaration = document.Declaration is null,
            };

            // A StringWriter reports UTF-16, which XmlWriter would copy into the declaration. The
            // body is sent as UTF-8, so the declaration has to say so.
            using var writer = new Utf8StringWriter();
            using (var xml = XmlWriter.Create(writer, settings))
            {
                document.Save(xml);
            }

            output = writer.ToString();
            return true;
        }
        catch (XmlException ex)
        {
            error = $"Invalid XML at line {ex.LineNumber}, position {ex.LinePosition}.";
            return false;
        }
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;
    }
}
