using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using QuizBuilder.App.Controls;
using QuizBuilder.App.ViewModels;

namespace QuizBuilder.App.Views;

public partial class TakeQuizWindow : Window
{
    private readonly TakeQuizViewModel _viewModel;

    // One drop target per sequence ListBox, created lazily. Each sequence
    // question renders its own ListBox from the same template, so they cannot
    // share a single target -- the gap arithmetic is relative to one list.
    private readonly Dictionary<ListBox, ListReorderDropTarget> _sequenceDrops = new();

    // Drag-in-progress state for the sequence list currently being dragged.
    private Point _seqDragStart;
    private TakeSequenceItemViewModel? _seqDragItem;
    private ListBox? _seqDragList;

    public TakeQuizWindow(TakeQuizViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        InitializeComponent();
        DataContext = viewModel;

        // Not resolved from DI: a sitting is a transient thing with a compiled
        // paper baked into it, and a singleton window would hand the next
        // attempt the previous one's answers.
    }

    /// <summary>
    /// True when the taker chose "save &amp; continue later". The Take tab reads
    /// this after the dialog closes to decide whether to persist a snapshot.
    /// </summary>
    public bool SaveRequested { get; private set; }

    private void OnSaveForLaterClick(object sender, RoutedEventArgs e)
    {
        // The snapshot itself is taken by the Take tab from the view model after
        // the window closes; here we only record the intent and close. Setting
        // the flag first means OnClosing knows the answers are being kept, not
        // lost, so it does not warn.
        SaveRequested = true;
        Close();
    }

    private void OnSubmitClick(object sender, RoutedEventArgs e)
    {
        var unanswered = _viewModel.Questions.Count(q => !q.IsAnswered);

        if (unanswered > 0)
        {
            var confirm = MessageBox.Show(
                unanswered == 1
                    ? "1 question has no answer. It will score nothing.\n\nSubmit anyway?"
                    : $"{unanswered} questions have no answer. They will score nothing.\n\nSubmit anyway?",
                "Submit answers",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Cancel);

            if (confirm != MessageBoxResult.OK) return;
        }

        _viewModel.Submit(timedOut: false);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing mid-attempt throws the attempt away. Say so: someone who has
        // answered twenty questions and clicks the X expecting to "pause" would
        // otherwise lose the lot with no warning. Skipped when saving for later,
        // where the answers are being kept, not lost.
        if (!SaveRequested && !_viewModel.IsSubmitted && _viewModel.Questions.Any(q => q.IsAnswered))
        {
            var confirm = MessageBox.Show(
                "Close without submitting? Your answers will be lost, and nothing will be recorded.",
                "Close quiz",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);

            if (confirm != MessageBoxResult.OK)
            {
                e.Cancel = true;
                return;
            }
        }

        // Stop the countdown either way: a timer left running holds a reference
        // to this window and keeps firing against a dead attempt.
        _viewModel.Cancel();

        base.OnClosing(e);
    }

    // --- Sequence drag and drop ---------------------------------------------
    //
    // Reuses the same ListReorderDropTarget the builder's section and question
    // lists use, so the gap arithmetic and the drag-down off-by-one fix are
    // shared rather than reimplemented. The one difference is that each sequence
    // question owns its own ListBox (rendered from a shared template), so the
    // list and its drop target are resolved from the event sender each time.

    private ListReorderDropTarget DropFor(ListBox list)
    {
        if (!_sequenceDrops.TryGetValue(list, out var target))
        {
            target = new ListReorderDropTarget(list);
            _sequenceDrops[list] = target;
        }

        return target;
    }

    private void OnSequencePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list) return;

        _seqDragStart = e.GetPosition(null);
        _seqDragList = list;
        _seqDragItem = ItemUnder(e.OriginalSource as DependencyObject);
    }

    private void OnSequenceMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not ListBox list || _seqDragItem is null || _seqDragList != list) return;

        var offset = _seqDragStart - e.GetPosition(null);
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(list, _seqDragItem, DragDropEffects.Move);

        // Blocking until the drag ends (including Escape / release outside the
        // list, where neither Drop nor DragLeave fires), so this is the only
        // reliable place to clear the line.
        DropFor(list).HideIndicator();
        _seqDragItem = null;
        _seqDragList = null;
    }

    private void OnSequenceDragOver(object sender, DragEventArgs e)
    {
        if (sender is not ListBox list) return;

        var valid = e.Data.GetDataPresent(typeof(TakeSequenceItemViewModel));
        e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;

        if (valid && DropFor(list).GapUnderPointer(e) is int gap)
            DropFor(list).ShowIndicator(gap);
        else
            DropFor(list).HideIndicator();

        e.Handled = true;
    }

    private void OnSequenceDragLeave(object sender, DragEventArgs e)
    {
        if (sender is ListBox list) DropFor(list).HideIndicator();
    }

    private void OnSequenceDrop(object sender, DragEventArgs e)
    {
        if (sender is not ListBox list) return;

        // Hide first and unconditionally: every early return below would
        // otherwise leave the line painted on the list.
        DropFor(list).HideIndicator();

        if (e.Data.GetData(typeof(TakeSequenceItemViewModel)) is not TakeSequenceItemViewModel dragged) return;
        if (list.DataContext is not TakeQuestionViewModel question) return;

        var oldIndex = question.SequenceItems.IndexOf(dragged);
        if (oldIndex < 0) return;

        if (DropFor(list).GapUnderPointer(e) is not int gap) return;

        // Gap is where the line was drawn; MoveSequenceItem is RemoveAt-then-
        // Insert, so convert to a post-removal index -- the same conversion the
        // builder's lists use.
        var newIndex = ListReorderDropTarget.GapToMoveIndex(gap, oldIndex);
        question.MoveSequenceItem(oldIndex, newIndex);
    }

    /// <summary>
    /// Walks up from the drag's origin to the <see cref="ListBoxItem"/> and
    /// returns its sequence item. Stops at any non-visual (a Run inside a
    /// TextBlock), which VisualTreeHelper would otherwise throw on.
    /// </summary>
    private static TakeSequenceItemViewModel? ItemUnder(DependencyObject? source)
    {
        while (source is not null and not ListBoxItem)
        {
            if (source is not Visual and not System.Windows.Media.Media3D.Visual3D)
                return null;

            source = VisualTreeHelper.GetParent(source);
        }

        return (source as ListBoxItem)?.DataContext as TakeSequenceItemViewModel;
    }
}
