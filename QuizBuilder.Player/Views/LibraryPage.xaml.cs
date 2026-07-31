using QuizBuilder.Player.ViewModels;

namespace QuizBuilder.Player.Views;

public partial class LibraryPage : ContentPage
{
    private readonly LibraryViewModel _vm;

    public LibraryPage(LibraryViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    // Rebuild the list each time the screen shows: returning from a quiz, or
    // after an import/delete, should reflect the current library.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.Refresh();
    }
}
