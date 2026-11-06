using System.Windows;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using RESTTest.Services;

namespace RESTTest.Controls;

/// <summary>
/// AvalonEdit configured for JSON, with a bindable <see cref="BoundText"/>.
/// <para>
/// AvalonEdit exposes <c>Text</c> as a plain CLR property (only Document and SyntaxHighlighting
/// are dependency properties), so MVVM binding needs this wrapper. Writes in both directions are
/// guarded against re-entrancy, and the caret is preserved so that a ViewModel echoing the text
/// back does not send the cursor to the start of the document on every keystroke.
/// </para>
/// </summary>
public class JsonEditor : TextEditor
{
    private const string HighlightingResource = "RESTTest.Resources.Json.xshd";

    private static readonly Lazy<IHighlightingDefinition?> JsonHighlighting = new(LoadHighlighting);

    public static readonly DependencyProperty BoundTextProperty = DependencyProperty.Register(
        nameof(BoundText),
        typeof(string),
        typeof(JsonEditor),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnBoundTextChanged));

    /// <summary>
    /// Nullable and checked before use: the base TextEditor constructor assigns a Document,
    /// which raises the virtual OnTextChanged before this type's constructor body has run.
    /// </summary>
    private BracketHighlightRenderer? _brackets;

    /// <summary>True while this control is the origin of a change, to break the update loop.</summary>
    private bool _isSynchronizing;

    /// <summary>
    /// Snapshot of the document for bracket matching, with the version it was taken at.
    /// Re-reading Document.Text on every caret move would copy the whole buffer each keystroke.
    /// </summary>
    private string _snapshot = string.Empty;

    private ITextSourceVersion? _snapshotVersion;

    public JsonEditor()
    {
        ShowLineNumbers = true;
        WordWrap = false;

        Options.IndentationSize = 2;
        Options.ConvertTabsToSpaces = true;
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.AllowScrollBelowDocument = false;
        // Off deliberately: the default current-line band is drawn with a fixed pen that reads
        // as a bright stripe on the dark theme.
        Options.HighlightCurrentLine = false;

        SyntaxHighlighting = JsonHighlighting.Value;

        _brackets = new BracketHighlightRenderer(TextArea.TextView);
        TextArea.Caret.PositionChanged += (_, _) => UpdateBracketMatch();

        // Ctrl+F, F3 and Esc are wired up by the panel itself.
        SearchPanel = SearchPanel.Install(this);
        SearchPanel.MarkerBrush = SearchMarkerBrush;
    }

    /// <summary>Highlight behind search hits; amber so it is distinct from the bracket pair.</summary>
    private static readonly Brush SearchMarkerBrush = CreateFrozenBrush("#D98A00", 0.45);

    /// <summary>The find bar, exposed so the view can restyle it for the current theme.</summary>
    public SearchPanel SearchPanel { get; }

    /// <summary>The document text, two-way bindable.</summary>
    public string BoundText
    {
        get => (string)GetValue(BoundTextProperty);
        set => SetValue(BoundTextProperty, value);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);

        if (_isSynchronizing)
        {
            return;
        }

        _isSynchronizing = true;
        try
        {
            // SetCurrentValue, not SetValue: assigning the local value would blow away the binding.
            SetCurrentValue(BoundTextProperty, Text);
        }
        finally
        {
            _isSynchronizing = false;
        }

        // Editing can change which pair the caret sits in without moving the caret.
        UpdateBracketMatch();
    }

    private void UpdateBracketMatch()
    {
        // Still inside the base constructor; there is nothing to draw on yet.
        if (_brackets is null)
        {
            return;
        }

        if (Document is not { } document)
        {
            _brackets.SetMatch(null);
            return;
        }

        RefreshSnapshot(document);

        _brackets.SetMatch(
            JsonBracketMatcher.TryFindMatch(_snapshot, CaretOffset, out var match) ? match : null);
    }

    private void RefreshSnapshot(TextDocument document)
    {
        var version = document.Version;

        if (version is not null
            && _snapshotVersion is not null
            && version.BelongsToSameDocumentAs(_snapshotVersion)
            && version.CompareAge(_snapshotVersion) == 0)
        {
            return;
        }

        _snapshot = document.Text;
        _snapshotVersion = version;
    }

    private static Brush CreateFrozenBrush(string hex, double opacity)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    private static void OnBoundTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not JsonEditor editor || editor._isSynchronizing)
        {
            return;
        }

        var text = e.NewValue as string ?? string.Empty;
        if (string.Equals(editor.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        editor._isSynchronizing = true;
        try
        {
            // Replacing the document resets the caret, so restore it. This matters because
            // Format JSON rewrites the whole body while the user is still in it.
            var caret = editor.CaretOffset;
            editor.Text = text;
            editor.CaretOffset = Math.Min(caret, text.Length);
        }
        finally
        {
            editor._isSynchronizing = false;
        }
    }

    private static IHighlightingDefinition? LoadHighlighting()
    {
        try
        {
            using var stream = typeof(JsonEditor).Assembly.GetManifestResourceStream(HighlightingResource);
            if (stream is null)
            {
                // Fall back to AvalonEdit's own definition rather than losing highlighting entirely.
                return HighlightingManager.Instance.GetDefinition("Json");
            }

            using var reader = new XmlTextReader(stream);
            return HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
