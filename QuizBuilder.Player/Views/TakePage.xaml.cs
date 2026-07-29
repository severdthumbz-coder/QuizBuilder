using QuizBuilder.Player.ViewModels;

namespace QuizBuilder.Player.Views;

public partial class TakePage : ContentPage
{
    public TakePage(TakeViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
