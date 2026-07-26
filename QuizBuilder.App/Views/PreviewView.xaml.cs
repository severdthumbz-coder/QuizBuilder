using System.Windows.Controls;
using QuizBuilder.App.ViewModels;

namespace QuizBuilder.App.Views;

public partial class PreviewView : UserControl
{
    private readonly PreviewViewModel _viewModel;

    public PreviewView(PreviewViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = _viewModel;

        // Settings live on another tab and raise no document event, so a
        // preview built earlier would silently show a paper compiled under the
        // old settings. Recompiling when the tab is shown is cheap and means
        // the paper always matches what Settings currently says.
        IsVisibleChanged += (_, e) =>
        {
            // Both branches matter. The shell keeps every tab alive and toggles
            // Visibility, so without the else the ViewModel would latch to
            // visible on first activation and never defer again -- the tab would
            // go on doing full rebuilds while the user typed on another tab.
            if (e.NewValue is true) _viewModel.OnActivated();
            else _viewModel.OnDeactivated();
        };
    }
}
