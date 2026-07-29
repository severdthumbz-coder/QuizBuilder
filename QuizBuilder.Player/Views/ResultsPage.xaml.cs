using QuizBuilder.Player.ViewModels;

namespace QuizBuilder.Player.Views;

public partial class ResultsPage : ContentPage
{
    public ResultsPage(ResultsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
