using System.Windows.Controls;
using QuizBuilder.App.ViewModels;

namespace QuizBuilder.App.Views;

public partial class ReviewView : UserControl
{
    private readonly ReviewViewModel _viewModel;

    public ReviewView(ReviewViewModel viewModel)
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
}
