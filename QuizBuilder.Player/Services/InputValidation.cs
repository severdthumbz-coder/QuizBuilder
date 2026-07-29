using System.Text.RegularExpressions;

namespace QuizBuilder.Player.Services;

/// <summary>
/// Small, dependency-free validators for the identity form. Kept here rather
/// than inline in the view model so the rules are testable and named.
/// </summary>
public static partial class InputValidation
{
    // A pragmatic email shape check: one @, a dotted domain, no spaces. Not
    // RFC-5322-complete (nothing short of sending mail is), but it catches the
    // typos that matter -- missing @, trailing comma, no TLD -- without
    // rejecting valid addresses. Deliverability is the mail app's problem.
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailShape();

    public static bool IsValidName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= 1;

    public static bool IsValidEmail(string? value) =>
        !string.IsNullOrWhiteSpace(value) && EmailShape().IsMatch(value.Trim());
}
