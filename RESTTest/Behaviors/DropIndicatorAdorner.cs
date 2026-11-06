using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using RESTTest.ViewModels;

namespace RESTTest.Behaviors;

/// <summary>
/// Draws the drop feedback over a sidebar row: an insertion line for a reorder, or a rounded
/// outline for dropping into a folder. Rendering in the adorner layer keeps it above the tree
/// without disturbing layout.
/// </summary>
public sealed class DropIndicatorAdorner : Adorner
{
    private static readonly Brush LineBrush = CreateBrush("#3B82D9", 1.0);
    private static readonly Brush FillBrush = CreateBrush("#3B82D9", 0.18);
    private static readonly Pen LinePen = CreatePen();

    private DropPlacement _placement;

    public DropIndicatorAdorner(UIElement adornedElement)
        : base(adornedElement)
    {
        // The adorner is decoration only; it must never swallow the drag events.
        IsHitTestVisible = false;
    }

    public DropPlacement Placement
    {
        get => _placement;
        set
        {
            if (_placement != value)
            {
                _placement = value;
                InvalidateVisual();
            }
        }
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = AdornedElement.RenderSize;

        if (_placement == DropPlacement.Inside)
        {
            drawingContext.DrawRoundedRectangle(
                FillBrush,
                LinePen,
                new Rect(0.5, 0.5, Math.Max(0, size.Width - 1), Math.Max(0, size.Height - 1)),
                4,
                4);
            return;
        }

        var y = _placement == DropPlacement.Before ? 1 : size.Height - 1;
        drawingContext.DrawLine(LinePen, new Point(0, y), new Point(size.Width, y));

        // Small end caps, so a 2px line still reads as an insertion point.
        drawingContext.DrawEllipse(LineBrush, null, new Point(2, y), 2.5, 2.5);
    }

    private static Brush CreateBrush(string hex, double opacity)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) { Opacity = opacity };
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen()
    {
        var pen = new Pen(CreateBrush("#3B82D9", 1.0), 2);
        pen.Freeze();
        return pen;
    }
}
