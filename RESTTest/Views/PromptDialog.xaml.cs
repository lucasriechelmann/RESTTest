using System.Windows;

namespace RESTTest.Views;

/// <summary>Single-line text prompt used for collection names.</summary>
public partial class PromptDialog
{
    public PromptDialog(string title, string label, string initialValue, string placeholder)
    {
        InitializeComponent();

        Title = title;
        LabelText.Text = label;
        ValueBox.Text = initialValue;
        ValueBox.PlaceholderText = placeholder;

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
