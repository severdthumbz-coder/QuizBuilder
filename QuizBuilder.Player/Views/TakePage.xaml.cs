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
    // starts the quiz from scratch (the reported bug). Now that pause/resume
    // exists, back offers three choices rather than the old leave/keep pair:
    // save the sitting to resume later, discard it, or stay.
    //
    // Returning true means "handled, do not navigate". We show the sheet
    // asynchronously and perform any nav ourselves once the user chooses;
    // returning true immediately keeps the page in place while it is up.
    protected override bool OnBackButtonPressed()
    {
        _ = ConfirmLeaveAsync();
        return true;
    }

    private async Task ConfirmLeaveAsync()
    {
        // A three-way choice needs an action sheet: a two-button alert cannot
        // express pause / discard / stay. "Keep going" is the cancel action, so
        // dismissing the sheet (tap-away / back again) is the safe no-op.
        const string pause = "Pause & save";
        const string leave = "Leave without saving";

        var choice = await DisplayActionSheetAsync(
            "Leave this quiz?",
            "Keep going",   // cancel
            null,           // no destructive slot; "leave" is a normal button
            pause,
            leave);

        if (choice == pause)
        {
            await _vm.PauseAndLeaveAsync();
        }
        else if (choice == leave)
        {
            // The user chose to discard; the session's take state is abandoned
            // deliberately. Pop back to the home hub.
            await Shell.Current.GoToAsync("..");
        }
        // "Keep going" or a dismissed sheet: stay on the take screen, nothing to do.
    }
}
