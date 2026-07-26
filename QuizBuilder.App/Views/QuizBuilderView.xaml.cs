using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using QuizBuilder.App.Controls;
using QuizBuilder.App.ViewModels;

namespace QuizBuilder.App.Views;

public partial class QuizBuilderView : UserControl
{
    private readonly QuizBuilderViewModel _viewModel;

    private Point _dragStart;
    private QuestionRowViewModel? _dragItem;
    private SectionViewModel? _dragSection;

    // Insertion-line indicators. Created after InitializeComponent so the
    // ListBoxes exist. Both lists share one implementation: duplicating the
    // gap arithmetic would let the two drift apart.
    private readonly ListReorderDropTarget _questionDrop;
    private readonly ListReorderDropTarget _sectionDrop;

    public QuizBuilderView(QuizBuilderViewModel viewModel)
    {
        InitializeComponent();

        _questionDrop = new ListReorderDropTarget(QuestionList);
        _sectionDrop = new ListReorderDropTarget(SectionList);

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        // The dialog lives here rather than in the view model, which has no
        // WPF dependency and should keep it: a MessageBox in a view model
        // makes every test that deletes a section need a message pump.
        _viewModel.ConfirmSectionDelete = ConfirmSectionDelete;

        DataContext = _viewModel;
    }

    /// <summary>
    /// Confirms deleting a section that still holds questions. Only reached
    /// when something would actually be lost -- an empty section deletes
    /// without a prompt, since prompting there trains the user to dismiss the
    /// dialog that matters.
    /// </summary>
    private static bool ConfirmSectionDelete(SectionDeleteRequest request)
    {
        var questions = request.QuestionCount == 1
            ? "its 1 question"
            : $"its {request.QuestionCount} questions";

        var answer = MessageBox.Show(
            $"Delete '{request.Title}'?\n\nThis also deletes {questions}.\n\nYou can undo this with Ctrl+Z.",
            "Delete section",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            // Cancel is the default so Enter or a stray double-click on the
            // Remove button cannot confirm the delete.
            MessageBoxResult.Cancel);

        return answer == MessageBoxResult.OK;
    }

    // --- Add question menu ---------------------------------------------------

    /// <summary>
    /// Built in code rather than XAML because the menu is generated from the
    /// ViewModel's type list. Duplicating those seven entries in markup would
    /// mean adding a question type in two places and forgetting one.
    /// </summary>
    private void OnAddQuestionClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();

        foreach (var option in _viewModel.QuestionTypes)
        {
            var header = new StackPanel();
            header.Children.Add(new TextBlock
            {
                Text = option.Label,
                FontWeight = FontWeights.SemiBold,
            });
            header.Children.Add(new TextBlock
            {
                Text = option.Description,
                Opacity = 0.7,
                FontSize = 11,
            });

            var item = new MenuItem
            {
                Header = header,
                Command = _viewModel.AddQuestionCommand,
                CommandParameter = option,
            };

            menu.Items.Add(item);
        }

        menu.PlacementTarget = AddQuestionButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        menu.IsOpen = true;
    }

    // --- File dialogs --------------------------------------------------------
    //
    // These live here, not in the ViewModel: opening a dialog from a ViewModel
    // would drag a WPF dependency into it and make it untestable. The ViewModel
    // exposes SaveToAsync/OpenAsync taking a path, and the view supplies one.

    private const string QbxFilter = "Quiz Builder session (*.qbx)|*.qbx|All files (*.*)|*.*";

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_viewModel.CurrentFilePath))
        {
            OnSaveAsClick(sender, e);
            return;
        }

        await _viewModel.SaveToAsync(_viewModel.CurrentFilePath);
    }

    private async void OnSaveAsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = QbxFilter,
            DefaultExt = ".qbx",
            AddExtension = true,
            FileName = SuggestFileName(),
            Title = "Save quiz session",
        };

        if (dialog.ShowDialog() != true) return;

        await _viewModel.SaveToAsync(dialog.FileName);
    }

    private async void OnOpenClick(object sender, RoutedEventArgs e)
    {
        // Losing unsaved work to a mis-click is unrecoverable, so ask first.
        if (_viewModel.IsDirty)
        {
            var answer = MessageBox.Show(
                "The current quiz has unsaved changes. Open a different file anyway?",
                "Unsaved changes",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes) return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = QbxFilter,
            DefaultExt = ".qbx",
            Title = "Open quiz session",
        };

        if (dialog.ShowDialog() != true) return;

        await _viewModel.OpenAsync(dialog.FileName);
    }

    private void OnAddQuestionImageClick(object sender, RoutedEventArgs e)
    {
        var editor = _viewModel.SelectedEditor;
        if (editor is null) return;

        var dialog = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All files|*.*",
            Title = "Add image to question",
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var bytes = System.IO.File.ReadAllBytes(dialog.FileName);
            editor.AttachImage(bytes, System.IO.Path.GetFileName(dialog.FileName));
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(
                $"Could not read that image: {ex.Message}",
                "Image", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Suggests a filename from the quiz title, stripped of characters Windows
    /// rejects. Without this, a title like "Chapter 3: Forces" produces an
    /// invalid filename and the dialog silently refuses to save.
    /// </summary>
    private string SuggestFileName()
    {
        var title = _viewModel.QuizTitle;
        if (string.IsNullOrWhiteSpace(title)) return "quiz.qbx";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(title.Where(c => !invalid.Contains(c)).ToArray()).Trim();

        return string.IsNullOrEmpty(cleaned) ? "quiz.qbx" : cleaned + ".qbx";
    }

    // --- Drag and drop -------------------------------------------------------

    private void OnQuestionPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragItem = ItemUnder(e.OriginalSource as DependencyObject);
    }

    private void OnQuestionMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem is null) return;

        // Require movement past the system drag threshold before starting a
        // drag. Without it, a plain click with a 1px wobble becomes a drag and
        // selecting a question by clicking becomes unreliable.
        var offset = _dragStart - e.GetPosition(null);
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(QuestionList, _dragItem, DragDropEffects.Move);

        // Blocking call: returns once the drag ends, including when it was
        // cancelled with Escape or released outside the list. Neither Drop nor
        // DragLeave fires in those cases, so this is the only reliable place
        // to guarantee the line is cleared.
        _questionDrop.HideIndicator();
        _dragItem = null;
    }

    private void OnQuestionDragOver(object sender, DragEventArgs e)
    {
        var valid = e.Data.GetDataPresent(typeof(QuestionRowViewModel));

        e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;

        if (valid && _questionDrop.GapUnderPointer(e) is int gap)
            _questionDrop.ShowIndicator(gap);
        else
            _questionDrop.HideIndicator();

        e.Handled = true;
    }

    private void OnQuestionDragLeave(object sender, DragEventArgs e) =>
        _questionDrop.HideIndicator();

    private void OnQuestionDrop(object sender, DragEventArgs e)
    {
        // Hide first and unconditionally: every early return below would
        // otherwise leave the line painted on the list.
        _questionDrop.HideIndicator();

        if (e.Data.GetData(typeof(QuestionRowViewModel)) is not QuestionRowViewModel dragged) return;
        if (_viewModel.SelectedSection is null) return;

        var oldIndex = _viewModel.Questions.IndexOf(dragged);
        if (oldIndex < 0) return;

        if (_questionDrop.GapUnderPointer(e) is not int gap) return;

        // The gap is where the line was drawn; Move* wants a post-removal
        // index. Converting is what makes the drop land where the line said.
        var newIndex = ListReorderDropTarget.GapToMoveIndex(gap, oldIndex);

        var sectionId = _viewModel.SelectedSection.Id;
        _viewModel.MoveQuestionTo(dragged.Id, sectionId, sectionId, newIndex);

        e.Handled = true;
    }

    /// <summary>
    /// Walks up from the hit-test source to the ListBoxItem, then reads its
    /// bound item. The event's OriginalSource is whatever visual was actually
    /// under the pointer -- usually a TextBlock several levels down.
    /// </summary>
    private static QuestionRowViewModel? ItemUnder(DependencyObject? source)
    {
        while (source is not null and not ListBoxItem)
        {
            // VisualTreeHelper throws on a non-Visual (e.g. a Run inside a
            // TextBlock), so stop rather than let it escape into the drag
            // handler.
            if (source is not Visual and not System.Windows.Media.Media3D.Visual3D)
                return null;

            source = VisualTreeHelper.GetParent(source);
        }

        return (source as ListBoxItem)?.DataContext as QuestionRowViewModel;
    }

    // --- Section drag-to-reorder -------------------------------------------
    // Mirrors the question drag handlers, with a separate drag field so a
    // section drag and a question drag never interfere. Sections reorder within
    // the one list (there is no cross-container move as there is for questions).

    private void OnSectionPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);

        // Only the grip starts a section drag. A section row's title is an
        // editable TextBox: pressing it surfaces the TextBox's inner text
        // layer, which is not a Visual, so SectionUnder cannot walk up to the
        // ListBoxItem and returns null -- the drag silently never started.
        // Requiring the grip also keeps click-to-rename and caret placement
        // intact, rather than making a press on the title ambiguous between
        // "select text" and "reorder section".
        _dragSection = IsSectionGrip(e.OriginalSource as DependencyObject)
            ? SectionUnder(e.OriginalSource as DependencyObject)
            : null;
    }

    /// <summary>
    /// True when the press landed on a section row's drag grip.
    /// <para>
    /// Matches on <see cref="FrameworkElement.Tag"/> rather than Name: inside a
    /// DataTemplate, x:Name does not generate a code-behind field, and relying
    /// on the templated Name surviving is a subtlety this does not need. Tag is
    /// a plain property set in the template and read here -- no ambiguity.
    /// </para>
    /// </summary>
    private static bool IsSectionGrip(DependencyObject? source) =>
        source is FrameworkElement fe && fe.Tag is string tag && tag == SectionGripTag;

    /// <summary>
    /// Marker linking the grip in the item template to the drag handler. Kept
    /// as a const so the XAML string and the check cannot drift apart silently.
    /// </summary>
    private const string SectionGripTag = "section-grip";

    private void OnSectionMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSection is null) return;

        // Same drag-threshold guard as questions: a click with a tiny wobble
        // must stay a click, or selecting a section becomes unreliable.
        var offset = _dragStart - e.GetPosition(null);
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(SectionList, _dragSection, DragDropEffects.Move);

        // See the question path: this covers a cancelled or aborted drag.
        _sectionDrop.HideIndicator();
        _dragSection = null;
    }

    private void OnSectionDragOver(object sender, DragEventArgs e)
    {
        var valid = e.Data.GetDataPresent(typeof(SectionViewModel));

        e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;

        if (valid && _sectionDrop.GapUnderPointer(e) is int gap)
            _sectionDrop.ShowIndicator(gap);
        else
            _sectionDrop.HideIndicator();

        e.Handled = true;
    }

    private void OnSectionDragLeave(object sender, DragEventArgs e) =>
        _sectionDrop.HideIndicator();

    private void OnSectionDrop(object sender, DragEventArgs e)
    {
        _sectionDrop.HideIndicator();

        if (e.Data.GetData(typeof(SectionViewModel)) is not SectionViewModel dragged) return;

        var oldIndex = _viewModel.Sections.IndexOf(dragged);
        if (oldIndex < 0) return;

        if (_sectionDrop.GapUnderPointer(e) is not int gap) return;

        var newIndex = ListReorderDropTarget.GapToMoveIndex(gap, oldIndex);

        _viewModel.MoveSectionTo(dragged.Id, newIndex);

        e.Handled = true;
    }

    private static SectionViewModel? SectionUnder(DependencyObject? source)
    {
        while (source is not null and not ListBoxItem)
        {
            if (source is not Visual and not System.Windows.Media.Media3D.Visual3D)
                return null;

            source = VisualTreeHelper.GetParent(source);
        }

        return (source as ListBoxItem)?.DataContext as SectionViewModel;
    }
}
