using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QuizBuilder.Player.Models;
using QuizBuilder.Player.Services;

namespace QuizBuilder.Player.ViewModels;

/// <summary>
/// Captures the taker's first name, last name and email before any quiz is
/// opened. Validation is per-field and surfaced inline next to the field (a
/// UI/UX Pro Max form rule), and errors are shown only after the field has been
/// touched, not on the empty initial state.
/// </summary>
public partial class IdentityViewModel : ObservableObject
{
    private readonly QuizSessionService _session;

    public IdentityViewModel(QuizSessionService session)
    {
        _session = session;

        // Pre-fill from a prior session so a taker who returns to the identity
        // screen isn't re-typing.
        if (_session.Identity is { } existing)
        {
            _firstName = existing.FirstName;
            _lastName = existing.LastName;
            _email = existing.Email;
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private string _firstName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private string _lastName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private string _email = string.Empty;

    // "Touched" flags: an error only shows once the taker has interacted with a
    // field (validate on blur, not on keystroke), so the form doesn't greet
    // them shouting about empty fields.
    [ObservableProperty] private bool _firstNameTouched;
    [ObservableProperty] private bool _lastNameTouched;
    [ObservableProperty] private bool _emailTouched;

    public bool FirstNameHasError => FirstNameTouched && !InputValidation.IsValidName(FirstName);
    public bool LastNameHasError => LastNameTouched && !InputValidation.IsValidName(LastName);
    public bool EmailHasError => EmailTouched && !InputValidation.IsValidEmail(Email);

    public string EmailErrorText =>
        string.IsNullOrWhiteSpace(Email) ? "Email is required." : "Enter a valid email address.";

    partial void OnFirstNameChanged(string value) => OnPropertyChanged(nameof(FirstNameHasError));
    partial void OnLastNameChanged(string value) => OnPropertyChanged(nameof(LastNameHasError));
    partial void OnEmailChanged(string value) => OnPropertyChanged(nameof(EmailHasError));
    partial void OnFirstNameTouchedChanged(bool value) => OnPropertyChanged(nameof(FirstNameHasError));
    partial void OnLastNameTouchedChanged(bool value) => OnPropertyChanged(nameof(LastNameHasError));
    partial void OnEmailTouchedChanged(bool value) => OnPropertyChanged(nameof(EmailHasError));

    public void MarkFirstNameTouched() => FirstNameTouched = true;
    public void MarkLastNameTouched() => LastNameTouched = true;
    public void MarkEmailTouched() => EmailTouched = true;

    private bool CanContinue =>
        InputValidation.IsValidName(FirstName) &&
        InputValidation.IsValidName(LastName) &&
        InputValidation.IsValidEmail(Email);

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private async Task ContinueAsync()
    {
        _session.SetIdentity(new TakerIdentity
        {
            FirstName = FirstName.Trim(),
            LastName = LastName.Trim(),
            Email = Email.Trim(),
        });

        await Shell.Current.GoToAsync("library");
    }
}
