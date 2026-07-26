using System.Windows;
using System.Windows.Controls;
using QuizBuilder.App.ViewModels;

namespace QuizBuilder.App.Views;

public partial class SettingsView : UserControl
{
    private readonly SettingsViewModel _viewModel;

    public SettingsView(SettingsViewModel viewModel)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        InitializeComponent();
        DataContext = viewModel;

        // Rebuild the per-section rows whenever the tab is shown: sections may
        // have been added, removed, or reordered on the Quiz Builder tab while
        // this tab was hidden.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) _viewModel.RefreshSections();
        };
    }
}
