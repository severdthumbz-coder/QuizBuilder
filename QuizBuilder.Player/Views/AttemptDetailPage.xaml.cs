using QuizBuilder.Player.ViewModels;

namespace QuizBuilder.Player.Views;

public partial class AttemptDetailPage : ContentPage
{
    public AttemptDetailPage(AttemptDetailViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
