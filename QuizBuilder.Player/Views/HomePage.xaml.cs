using QuizBuilder.Player.ViewModels;

namespace QuizBuilder.Player.Views;

public partial class HomePage : ContentPage
{
    private readonly HomeViewModel _vm;

    public HomePage(HomeViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Attach();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.Detach();
    }
}
