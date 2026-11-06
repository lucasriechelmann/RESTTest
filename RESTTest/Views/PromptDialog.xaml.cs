using System.Windows;

namespace RESTTest.Views;

/// <summary>
/// Text prompt: a single line for names, or a larger box for pasting a block of text (a cURL
/// command). In multi-line mode Enter inserts a newline, so accepting is done with the OK button.
/// </summary>
public partial class PromptDialog
{
    public PromptDialog(string title, string label, string initialValue, string placeholder, bool multiline = false)
    {
        InitializeComponent();

        Title = title;
        LabelText.Text = label;
        ValueBox.Text = initialValue;
        ValueBox.PlaceholderText = placeholder;

        if (multiline)
        {
            Width = 680;
            Height = 440;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            MinWidth = 480;
            MinHeight = 320;

            ValueBox.AcceptsReturn = true;
            ValueBox.TextWrapping = TextWrapping.Wrap;
            ValueBox.VerticalContentAlignment = VerticalAlignment.Top;
            ValueBox.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
            ValueBox.Height = 240;
        }

        // Preselect so typing replaces the suggested name immediately.
        Loaded += (_, _) =>
        {
            ValueBox.Focus();
            ValueBox.SelectAll();
        };
    }

    public string Value => ValueBox.Text;

    private void OnAccept(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
