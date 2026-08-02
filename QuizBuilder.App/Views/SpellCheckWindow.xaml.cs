using System.Windows;
using System.Windows.Controls;
using QuizBuilder.App.ViewModels;

namespace QuizBuilder.App.Views;

/// <summary>
/// The modal spell-check review dialog. Presents issues grouped by section and
/// applies Replace / Ignore. The ViewModel does the work; this code-behind only
/// forwards the button clicks (each row carries its own VM in Tag) and closes.
/// </summary>
public partial class SpellCheckWindow : Window
{
    private readonly SpellCheckViewModel _viewModel;

    public SpellCheckWindow(SpellCheckViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnReplaceClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is SpellIssueRowViewModel row)
            _viewModel.Replace(row);
    }

    private void OnIgnoreClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is SpellIssueRowViewModel row)
            _viewModel.Ignore(row);
    }

    private void OnRecheckClick(object sender, RoutedEventArgs e) => _viewModel.Run();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
