using QuizBuilder.Player.ViewModels;

namespace QuizBuilder.Player.Views;

public partial class ReviewPage : ContentPage
{
    public ReviewPage(ReviewViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
