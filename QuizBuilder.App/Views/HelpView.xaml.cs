using System.Windows.Controls;
using QuizBuilder.App.ViewModels;

namespace QuizBuilder.App.Views;

public partial class HelpView : UserControl
{
    /// <summary>
    /// Constructor injection, per the shell's chosen pattern: the ViewModel is
    /// resolved by DI and set as DataContext here. A DataTemplate-based
    /// approach would need a parameterless constructor and would fail silently
    /// if the template mapping were wrong; this throws at resolve time instead.
    /// </summary>
    public HelpView(HelpViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }
}
