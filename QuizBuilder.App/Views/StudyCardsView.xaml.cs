using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using QuizBuilder.App.ViewModels;

namespace QuizBuilder.App.Views;

public partial class StudyCardsView : UserControl
{
    private readonly StudyCardsViewModel _viewModel;

    public StudyCardsView(StudyCardsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        InitializeComponent();
        DataContext = viewModel;

        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) _viewModel.OnActivated();
            else _viewModel.OnDeactivated();
        };
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
