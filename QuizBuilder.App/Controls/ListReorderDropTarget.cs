using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace QuizBuilder.App.Controls;

/// <summary>
/// Shared drag-reorder plumbing for a <see cref="ListBox"/>: works out which
/// gap the pointer is over, drives an <see cref="InsertionLineAdorner"/>, and
/// converts the gap into the index Core's Move* methods expect.
/// <para>
/// Sections and questions both use this. Implementing the gap arithmetic twice
/// would guarantee the two lists drift apart.
/// </para>
/// </summary>
public sealed class ListReorderDropTarget
{
    private readonly ListBox _list;
    private InsertionLineAdorner? _adorner;

    public ListReorderDropTarget(ListBox list) =>
        _list = list ?? throw new ArgumentNullException(nameof(list));

    /// <summary>
    /// Gap index the pointer is currently over: 0..Items.Count, where g means
    /// "insert before row g" and Items.Count means "after the last row".
    /// Returns null when the list has no rows.
    /// </summary>
    public int? GapUnderPointer(DragEventArgs e)
    {
        var count = _list.Items.Count;
        if (count == 0) return null;

        for (var i = 0; i < count; i++)
        {
            if (_list.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem row) continue;
            if (!row.IsVisible) continue;

            var topLeft = row.TranslatePoint(new Point(0, 0), _list);
            var height = row.ActualHeight;
            var y = e.GetPosition(_list).Y;

            if (y < topLeft.Y || y > topLeft.Y + height) continue;

            // Above the midpoint drops before this row, below drops after it.
            return y < topLeft.Y + (height / 2) ? i : i + 1;
        }

        // Past the last row (or in padding between rows): append.
        return count;
    }

    /// <summary>
    /// Shows the insertion line for <paramref name="gap"/>, creating the
    /// adorner on first use.
    /// </summary>
    public void ShowIndicator(int gap)
    {
        var layer = AdornerLayer.GetAdornerLayer(_list);
        if (layer is null) return;

        if (_adorner is null)
        {
            var brush = _list.TryFindResource("Brush.Accent") as Brush ?? Brushes.DodgerBlue;
            _adorner = new InsertionLineAdorner(_list, brush);
            layer.Add(_adorner);
        }

        _adorner.ShowAt(GapOffset(gap));
    }

    /// <summary>
    /// Hides the line. Must be called on drop, on drag-leave, and when a drag
    /// is cancelled -- a stale adorner left painted on the list is the classic
    /// failure of this feature.
    /// </summary>
    public void HideIndicator() => _adorner?.HideLine();

    /// <summary>
    /// Converts a gap index into the index <c>MoveSection</c> /
    /// <c>MoveQuestion</c> expect.
    /// <para>
    /// Those methods are RemoveAt-then-Insert, so the index is interpreted
    /// <i>after</i> the dragged row has been taken out. Every gap beyond the
    /// row's old position therefore shifts down by one. Verified exhaustively
    /// for lists of 1..11 items against a physical remove-then-splice model.
    /// </para>
    /// </summary>
    public static int GapToMoveIndex(int gap, int oldIndex) =>
        gap > oldIndex ? gap - 1 : gap;

    /// <summary>
    /// Y offset of a gap, in the list's coordinates: the top of row
    /// <paramref name="gap"/>, or the bottom of the last row when the gap is
    /// past the end.
    /// </summary>
    private double GapOffset(int gap)
    {
        var count = _list.Items.Count;
        if (count == 0) return 0;

        if (gap >= count)
        {
            if (_list.ItemContainerGenerator.ContainerFromIndex(count - 1) is ListBoxItem last)
                return last.TranslatePoint(new Point(0, 0), _list).Y + last.ActualHeight;

            return 0;
        }

        if (_list.ItemContainerGenerator.ContainerFromIndex(gap) is ListBoxItem row)
            return row.TranslatePoint(new Point(0, 0), _list).Y;

        return 0;
    }
}
