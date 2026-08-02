using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using QuizBuilder.App.Services;
using QuizBuilder.App.ViewModels;
using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.App.Views;

public partial class StudyCardsView : UserControl
{
    private readonly StudyCardsViewModel _viewModel;

    private readonly IQuizDocumentService _document;
    private readonly ITextReviewProvider _reviewProvider;
    private readonly SpellIgnoreListStore _ignoreList;
    private readonly SpellFixApplier _fixApplier;

    public StudyCardsView(
        StudyCardsViewModel viewModel,
        IQuizDocumentService document,
        ITextReviewProvider reviewProvider,
        SpellIgnoreListStore ignoreList,
        SpellFixApplier fixApplier)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _reviewProvider = reviewProvider ?? throw new ArgumentNullException(nameof(reviewProvider));
        _ignoreList = ignoreList ?? throw new ArgumentNullException(nameof(ignoreList));
        _fixApplier = fixApplier ?? throw new ArgumentNullException(nameof(fixApplier));

        InitializeComponent();
        DataContext = viewModel;

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) _viewModel.OnActivated();
            else _viewModel.OnDeactivated();
        };
    }

    /// <summary>
    /// Opens the same whole-quiz spell-check review as the Quiz Builder tab. The
    /// study-card text is included in that scan (under the quiz-level group), so
    /// this is a convenience entry point from the tab where an author is working
    /// on cards, not a separate cards-only checker.
    /// </summary>
    private void OnCheckSpellingClick(object sender, RoutedEventArgs e)
    {
        var vm = new SpellCheckViewModel(_document, _reviewProvider, _ignoreList, _fixApplier);
        var window = new SpellCheckWindow(vm) { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }

    // The Tag carries the row the button belongs to, so one dialog helper serves
    // every card without wiring a command parameter through the template.
    private void OnAddFrontImageClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is StudyCardRowViewModel row)
            AttachImage((data, name) => row.AttachFrontImage(data, name));
    }

    private void OnAddBackImageClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is StudyCardRowViewModel row)
            AttachImage((data, name) => row.AttachBackImage(data, name));
    }

    private static void AttachImage(System.Action<byte[], string> attach)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All files|*.*",
            Title = "Add image to study card",
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var data = System.IO.File.ReadAllBytes(dialog.FileName);
            attach(data, System.IO.Path.GetFileName(dialog.FileName));
        }
        catch (System.Exception ex)
        {
            MessageBox.Show(
                $"Could not read that image: {ex.Message}",
                "Image", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
