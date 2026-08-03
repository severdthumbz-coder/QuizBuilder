using System.Windows;
using System.Windows.Controls;
using QuizBuilder.App.ViewModels;

namespace QuizBuilder.App.Views;

/// <summary>
/// The AI grammar-check dialog. The ViewModel holds the state and the async run;
/// this code-behind awaits <see cref="GrammarCheckViewModel.RunAsync"/> from the
/// Run button (the house pattern for async in this app — see GitHub connect),
/// and forwards the per-row Accept/Reject clicks (each row is in the button Tag).
/// </summary>
public partial class GrammarCheckWindow : Window
{
    private readonly GrammarCheckViewModel _viewModel;

    public GrammarCheckWindow(GrammarCheckViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void OnRunClick(object sender, RoutedEventArgs e) =>
        await _viewModel.RunAsync();

    private void OnCancelClick(object sender, RoutedEventArgs e) => _viewModel.Cancel();

    private void OnAcceptClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is GrammarSuggestionRow row)
            _viewModel.Accept(row);
    }

    private void OnRejectClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is GrammarSuggestionRow row)
            _viewModel.Reject(row);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        _viewModel.Cancel(); // stop any in-flight check
        Close();
    }
}
