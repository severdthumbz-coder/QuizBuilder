using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace QuizBuilder.App.Controls;

/// <summary>
/// Draws a horizontal insertion line across a ListBox during a drag, showing
/// which gap the dragged row will land in.
/// <para>
/// A line in the gap is deliberate: highlighting the target row instead reads
/// as "replace this one", which is not what a reorder does.
/// </para>
/// <para>
/// Adorners render on the AdornerLayer, above the adorned element, and do not
/// take part in hit-testing (<see cref="IsHitTestVisible"/> is false below), so
/// the line cannot swallow the drop it is describing.
/// </para>
/// </summary>
public sealed class InsertionLineAdorner : Adorner
{
    private readonly Pen _pen;
    private double _y;
    private bool _visible;

    public InsertionLineAdorner(UIElement adornedElement, Brush lineBrush)
        : base(adornedElement)
    {
        // Not hit-test visible: the line is pure feedback. If it absorbed the
        // pointer, DragOver would stop firing over the line itself and the
        // indicator would flicker as the user moved across it.
        IsHitTestVisible = false;

        var brush = lineBrush;
        if (brush.CanFreeze) brush = brush.Clone();
        brush.Freeze();

        _pen = new Pen(brush, 2);
        _pen.Freeze();
    }

    /// <summary>
    /// Places the line at <paramref name="y"/> (in the adorned element's
    /// coordinates) and shows it. Repeated calls with the same value are cheap:
    /// the redraw is skipped unless something actually moved.
    /// </summary>
    public void ShowAt(double y)
    {
        if (_visible && Math.Abs(_y - y) < 0.5) return;

        _y = y;
        _visible = true;
        InvalidateVisual();
    }

    /// <summary>Hides the line. Safe to call when already hidden.</summary>
    public void HideLine()
    {
        if (!_visible) return;

        _visible = false;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (!_visible) return;

        var width = AdornedElement.RenderSize.Width;
        if (width <= 0) return;

        // Snap to a whole pixel. A 2px line drawn on a half pixel renders as
        // two washed-out 1px lines, which looks like a rendering bug.
        var y = Math.Round(_y) + 0.5;

        drawingContext.DrawLine(_pen, new Point(0, y), new Point(width, y));

        // Small end caps, so the line reads as an insertion marker rather than
        // a divider or a border that happens to be there.
        const double cap = 3;
        drawingContext.DrawLine(_pen, new Point(0.5, y - cap), new Point(0.5, y + cap));
        drawingContext.DrawLine(_pen, new Point(width - 0.5, y - cap), new Point(width - 0.5, y + cap));
    }
}
