using QuizBuilder.Core.Services;
using QuizBuilder.Core.Theming;

namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// Exports a quiz as a single self-contained HTML file that grades itself in the
/// browser -- inputs, a submit button, and a client-side grader that mirrors the
/// in-app one.
///
/// Distinct from IHtmlExporter, which produces a printable paper. That one omits
/// the answer key by default, because you do not hand students the answers. This
/// one MUST embed the key: the browser has no server to grade against, so the
/// grader and the correct answers live in the page. A consequence worth being
/// honest about -- and the exported page says so -- is that anyone who views the
/// page source can read the answers. That is fine for self-assessment and
/// practice, which is what this is for; it is not an exam invigilation tool.
/// </summary>
public interface IQuizWebExporter
{
    /// <summary>
    /// Renders the self-grading page.
    ///
    /// The grade the browser computes must match what the app's QuizGrader would
    /// give for the same answers. Both were ported to a reference model and
    /// checked to agree on a battery covering every rule and the essay-exclusion
    /// and pass-boundary cases; the embedded JavaScript mirrors that model.
    /// </summary>
    string Render(CompiledQuiz quiz, ThemeTokens theme, WebExportOptions options);
}

public sealed class WebExportOptions
{
    /// <summary>
    /// The pass mark, so the browser can show pass/fail the same way the app
    /// does. These come from the quiz settings, not invented here.
    /// </summary>
    public int PassPercentage { get; set; } = 50;

    /// <summary>Whether pass/fail is judged on points or on question count.</summary>
    public bool PassOnQuestionCount { get; set; } = true;

    /// <summary>
    /// The time limit in minutes, or null for none. The browser mirrors the app:
    /// a countdown that auto-submits when it reaches zero, so the limit means the
    /// same thing whether the quiz is taken in the app or the browser.
    /// </summary>
    public int? TimeLimitMinutes { get; set; }

    /// <summary>Resolves an image path to a data: URI for the single-file page, or null.</summary>
    public Func<string?, string?>? ImageDataUriResolver { get; set; }
}
