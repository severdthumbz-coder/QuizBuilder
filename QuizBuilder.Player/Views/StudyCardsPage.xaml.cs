using QuizBuilder.Player.ViewModels;

namespace QuizBuilder.Player.Views;

public partial class StudyCardsPage : ContentPage
{
    public StudyCardsPage(StudyCardsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
