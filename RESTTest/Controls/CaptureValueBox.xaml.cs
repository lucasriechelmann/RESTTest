using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace RESTTest.Controls;

/// <summary>
/// A value box that can switch to "from capture": a text box, or an editable list of capture
/// names, plus a toggle. Bindable properties only, no behaviour, so it is reused by every value
/// field that may hold a capture (query, header, form and path rows, and the Auth secrets).
/// </summary>
public partial class CaptureValueBox : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(CaptureValueBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty UsesCaptureProperty = DependencyProperty.Register(
        nameof(UsesCapture), typeof(bool), typeof(CaptureValueBox),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty CaptureNameProperty = DependencyProperty.Register(
        nameof(CaptureName), typeof(string), typeof(CaptureValueBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty CaptureNamesProperty = DependencyProperty.Register(
        nameof(CaptureNames), typeof(IEnumerable), typeof(CaptureValueBox), new PropertyMetadata(null));

    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(CaptureValueBox), new PropertyMetadata(string.Empty));

    public CaptureValueBox()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool UsesCapture
    {
        get => (bool)GetValue(UsesCaptureProperty);
        set => SetValue(UsesCaptureProperty, value);
    }

    public string CaptureName
    {
        get => (string)GetValue(CaptureNameProperty);
        set => SetValue(CaptureNameProperty, value);
    }

    public IEnumerable? CaptureNames
    {
        get => (IEnumerable?)GetValue(CaptureNamesProperty);
        set => SetValue(CaptureNamesProperty, value);
    }

    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }
}
