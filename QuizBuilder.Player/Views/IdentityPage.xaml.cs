using QuizBuilder.Player.ViewModels;

namespace QuizBuilder.Player.Views;

public partial class IdentityPage : ContentPage
{
    private readonly IdentityViewModel _vm;

    public IdentityPage(IdentityViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    // Validate-on-blur: mark the field touched when focus leaves it, which is
    // when the inline error (if any) becomes visible. Keystroke-time validation
    // would nag while the user is still typing.
    private void OnFirstNameUnfocused(object? sender, FocusEventArgs e) => _vm.MarkFirstNameTouched();
    private void OnLastNameUnfocused(object? sender, FocusEventArgs e) => _vm.MarkLastNameTouched();
    private void OnEmailUnfocused(object? sender, FocusEventArgs e) => _vm.MarkEmailTouched();
}
