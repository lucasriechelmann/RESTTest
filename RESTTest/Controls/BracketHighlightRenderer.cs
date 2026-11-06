using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using RESTTest.Services;

namespace RESTTest.Controls;

/// <summary>
/// Paints a rounded box behind the bracket pair around the caret. Drawn into the selection
/// layer so it sits under the glyphs and under the selection highlight.
/// </summary>
public sealed class BracketHighlightRenderer : IBackgroundRenderer
{
    private static readonly Brush Fill = CreateBrush("#3B82D9", 0.22);
    private static readonly Pen Outline = CreatePen("#3B82D9", 0.85);

    private readonly TextView _textView;

    private BracketMatch? _match;

    public BracketHighlightRenderer(TextView textView)
    {
        _textView = textView;
        _textView.BackgroundRenderers.Add(this);
    }

    public KnownLayer Layer => KnownLayer.Selection;

    /// <summary>Sets the pair to highlight, redrawing only when it actually changed.</summary>
    public void SetMatch(BracketMatch? match)
    {
        if (Nullable.Equals(_match, match))
        {
            return;
        }

        _match = match;
        _textView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (_match is not { } match || !textView.VisualLinesValid || textView.VisualLines.Count == 0)
        {
            return;
        }

        DrawBracket(textView, drawingContext, match.OpenOffset);
        DrawBracket(textView, drawingContext, match.CloseOffset);
    }

    private static void DrawBracket(TextView textView, DrawingContext drawingContext, int offset)
    {
        var builder = new BackgroundGeometryBuilder
        {
            AlignToWholePixels = true,
            CornerRadius = 2,
        };

        // A single character wide. Offsets scrolled out of view simply produce no geometry.
        builder.AddSegment(textView, new TextSegment { StartOffset = offset, EndOffset = offset + 1 });

        if (builder.CreateGeometry() is { } geometry)
        {
            drawingContext.DrawGeometry(Fill, Outline, geometry);
        }
    }

    private static Brush CreateBrush(string hex, double opacity)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(string hex, double opacity)
    {
        var pen = new Pen(CreateBrush(hex, opacity), 1);
        pen.Freeze();
        return pen;
    }
}
