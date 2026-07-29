namespace QuizBuilder.Player.Models;

/// <summary>
/// Who is taking the quiz and where their results should be emailed.
///
/// Captured once on the identity screen and carried for the whole session. The
/// email is a free-choice recipient (self or an instructor), so the app makes
/// no assumption about whose address it is -- it simply pre-fills the native
/// mail composer's "To" field with whatever was entered.
/// </summary>
public sealed class TakerIdentity
{
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;

    public string FullName => $"{FirstName} {LastName}".Trim();

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(FirstName) &&
        !string.IsNullOrWhiteSpace(LastName) &&
        !string.IsNullOrWhiteSpace(Email);
}
