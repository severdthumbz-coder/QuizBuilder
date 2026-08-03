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
            if (e.NewValue is true)
            {
                _viewModel.RefreshSections();
                _viewModel.RefreshSpellWords();
            }
        };
    }

    /// <summary>
    /// Reads the AI key from the PasswordBox and hands the plaintext straight to
    /// the view model, which encrypts and stores it. The key is never bound to a
    /// property or held in a field — same discipline as the GitHub token — so a
    /// binding, debugger dump, or crash report can't pick it up. The box is
    /// cleared immediately after.
    /// </summary>
    private void OnSaveAiKeyClick(object sender, RoutedEventArgs e)
    {
        var key = AiKeyBox.Password;
        if (!string.IsNullOrWhiteSpace(key))
        {
            _viewModel.SetAiKey(key);
            AiKeyBox.Clear();
        }
    }
}
