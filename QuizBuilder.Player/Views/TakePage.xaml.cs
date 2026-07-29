using QuizBuilder.Player.ViewModels;

namespace QuizBuilder.Player.Views;

public partial class TakePage : ContentPage
{
    private readonly TakeViewModel _vm;

    public TakePage(TakeViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    // Intercept the Android hardware / gesture back button. Without this, back
    // pops the take page and destroys the in-progress TakeSession, so returning
    // starts the quiz from scratch (the reported bug). Until full pause/resume
    // exists, the safe behaviour is to confirm before leaving so a stray back
    // tap can't silently discard an attempt.
    //
    // Returning true means "handled, do not navigate". We show the confirm
    // dialog asynchronously and, only if the user confirms, perform the nav
    // ourselves. Returning true immediately keeps the page in place while the
    // dialog is up.
    protected override bool OnBackButtonPressed()
    {
        _ = ConfirmLeaveAsync();
        return true;
    }

    private async Task ConfirmLeaveAsync()
    {
        var leave = await DisplayAlertAsync(
            "Leave quiz?",
            "Your progress in this quiz will be lost. Leave anyway?",
            "Leave", "Keep going");

        if (leave)
        {
            // Pop back to the home hub. The session's take state is abandoned
            // deliberately (the user chose to leave); a future pause/resume
            // feature will offer to snapshot it instead.
            await Shell.Current.GoToAsync("..");
        }
    }
}
