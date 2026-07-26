using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Theming;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Renders a compiled quiz as a single self-contained HTML file.
///
/// Self-contained matters: the output is meant to be emailed, uploaded, or
/// opened from a USB stick. A page that needs a sibling .css file arrives
/// broken, and the person who receives it has no way to tell why.
///
/// This reads the same <see cref="CompiledQuiz"/> the Preview tab shows, so a
/// printed paper cannot disagree with what was checked on screen. It also reads
/// the same <see cref="ThemeTokens"/> the WPF layer binds to -- which is why
/// those tokens are plain POCOs in Core storing CSS-order hex, rather than WPF
/// Color objects that would need a parallel copy here.
///
/// PDF is deliberately not a dependency: the browser's own print engine
/// paginates better than a hand-rolled layout, honours the @media print rules
/// below, and costs nothing in licensing.
/// </summary>
public sealed partial class HtmlExporter : IHtmlExporter
{
    [GeneratedRegex(@"^#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex HexColorRegex();

    /// <summary>
    /// A CSS font-family list: comma-separated items, each either a quoted
    /// string or one or more identifiers. Matches "Georgia, Cambria, serif",
    /// "\"Times New Roman\", serif" and "-apple-system, BlinkMacSystemFont".
    /// </summary>
    [GeneratedRegex("""
        ^\s*
        (?:"[^"<>{};]*"|'[^'<>{};]*'|-?[A-Za-z_][A-Za-z0-9_-]*(?:\s+-?[A-Za-z_][A-Za-z0-9_-]*)*)
        (?:\s*,\s*
        (?:"[^"<>{};]*"|'[^'<>{};]*'|-?[A-Za-z_][A-Za-z0-9_-]*(?:\s+-?[A-Za-z_][A-Za-z0-9_-]*)*))*
        \s*$
        """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex FontFamilyRegex();

    public string Render(CompiledQuiz quiz, ThemeTokens theme, HtmlExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(quiz);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(options);

        var sb = new StringBuilder();

        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"<title>{Escape(quiz.Title)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine(BuildCss(theme));
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        AppendPrintBar(sb, options);

        sb.AppendLine("<main class=\"paper\">");
        AppendHeader(sb, quiz, options);

        foreach (var section in quiz.Sections)
            AppendSection(sb, section, options);

        sb.AppendLine("</main>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    // --- CSS ----------------------------------------------------------------

    /// <summary>
    /// A theme's colours are user-editable, so a token could hold anything at
    /// all. Emitted raw into a style block, a value like
    /// "red; } body { display: none } .x {" breaks out of its declaration and
    /// rewrites the page. Every colour goes through this.
    /// </summary>
    private static string Color(string? value, string fallback)
        => value is not null && HexColorRegex().IsMatch(value) ? value : fallback;

    /// <summary>
    /// Validates a font-family list against the CSS grammar and falls back on
    /// anything else -- the same posture as <see cref="Color"/>.
    ///
    /// An earlier version sanitised by DELETING metacharacters instead. That
    /// blocked the injection, but left the attack text sitting in the output as
    /// a garbage declaration ("font-family: Georgia  body  display: none  .x"),
    /// which is neither the author's font nor a working fallback. Stripping
    /// characters until something looks safe is guesswork; matching a grammar
    /// and rejecting the rest is not.
    /// </summary>
    private static string FontFamily(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;

        var trimmed = value.Trim();
        return FontFamilyRegex().IsMatch(trimmed) ? trimmed : fallback;
    }

    private static string Num(double value)
        // Invariant culture: on a machine with a comma decimal separator,
        // "14,5px" is not a CSS length and the rule is silently dropped.
        => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string BuildCss(ThemeTokens theme)
    {
        var c = theme.Colors;
        var t = theme.Typography;
        var s = theme.Spacing;
        var shape = theme.Shape;

        return $$"""
            :root {
              --bg: {{Color(c.Background, "#FFFFFF")}};
              --surface: {{Color(c.Surface, "#FFFFFF")}};
              --sunken: {{Color(c.SurfaceSunken, "#F2F2F2")}};
              --primary: {{Color(c.Primary, "#1F3A5F")}};
              --text: {{Color(c.TextPrimary, "#1A1A1A")}};
              --text-2: {{Color(c.TextSecondary, "#4A4A4A")}};
              --border: {{Color(c.Border, "#D0D0D0")}};
              --success: {{Color(c.Success, "#2E6B4F")}};
              --radius: {{Num(shape.RadiusMd)}}px;
              --unit: {{Num(s.Unit)}}px;
            }

            * { box-sizing: border-box; }

            body {
              margin: 0;
              background: var(--sunken);
              color: var(--text);
              font-family: {{FontFamily(t.FontFamily, "Georgia, serif")}};
              font-size: {{Num(t.Body)}}px;
              line-height: {{Num(t.LineHeightBody)}};
            }

            .paper {
              max-width: 820px;
              margin: {{Num(s.Lg)}}px auto;
              padding: {{Num(s.Xxl)}}px;
              background: var(--bg);
              border: 1px solid var(--border);
              border-radius: var(--radius);
            }

            h1 {
              font-size: {{Num(t.Title)}}px;
              font-weight: {{t.WeightBold}};
              line-height: {{Num(t.LineHeightHeading)}};
              margin: 0;
            }

            h2 {
              font-size: {{Num(t.Subtitle)}}px;
              font-weight: {{t.WeightBold}};
              line-height: {{Num(t.LineHeightHeading)}};
              margin: 0 0 {{Num(s.Sm)}}px 0;
            }

            .description { color: var(--text-2); margin: {{Num(s.Xs)}}px 0 0 0; }
            ul.description { padding-left: {{Num(s.Lg)}}px; }
            ul.description li { margin: {{Num(s.Xs)}}px 0; }
            .meta { color: var(--text-2); font-size: {{Num(t.Caption)}}px; margin-top: {{Num(s.Xs)}}px; }
            .rule { border: 0; border-top: 1px solid var(--border); margin: {{Num(s.Md)}}px 0 {{Num(s.Lg)}}px 0; }

            .section { margin-bottom: {{Num(s.Xl)}}px; }
            .section-head { display: flex; justify-content: space-between; align-items: baseline; gap: {{Num(s.Md)}}px; }
            .section-points { color: var(--text-2); font-size: {{Num(t.Caption)}}px; white-space: nowrap; }
            .empty-section { color: var(--text-2); font-style: italic; font-size: {{Num(t.Caption)}}px; }

            .question { margin-bottom: {{Num(s.Lg)}}px; }
            .question-head { display: flex; gap: {{Num(s.Xs)}}px; align-items: baseline; }
            .number { font-weight: {{t.WeightBold}}; min-width: {{Num(s.Lg)}}px; }
            .prompt { flex: 1; }
            .question-image { display: block; max-width: 100%; height: auto; margin: {{Num(s.Sm)}}px 0; border-radius: 4px; }
            .points { color: var(--text-2); font-size: {{Num(t.Caption)}}px; white-space: nowrap; }

            .options { list-style: none; padding: 0; margin: {{Num(s.Xs)}}px 0 0 {{Num(s.Lg)}}px; }
            .option { display: flex; gap: {{Num(s.Xs)}}px; align-items: baseline; margin-bottom: {{Num(s.Xxs)}}px; }
            .option-label { min-width: {{Num(s.Md)}}px; }

            /* The tick marks the answer by SHAPE. A colour-only cue disappears
               in a monochrome printout and is invisible to a colour-blind
               reader -- and this page exists to be printed. */
            .correct-mark { color: var(--success); font-weight: {{t.WeightBold}}; }

            .match-row { display: flex; gap: {{Num(s.Xs)}}px; align-items: baseline; margin-bottom: {{Num(s.Xxs)}}px; }
            .match-blank { border-bottom: 1px solid var(--border); min-width: {{Num(s.Xl)}}px; }

            .lines { margin: {{Num(s.Xs)}}px 0 0 {{Num(s.Lg)}}px; }
            .line { border-bottom: 1px solid var(--border); height: {{Num(s.Lg)}}px; }

            .hint { color: var(--text-2); font-size: {{Num(t.Caption)}}px; font-style: italic; margin: {{Num(s.Xxs)}}px 0 0 {{Num(s.Lg)}}px; }

            .answer {
              margin: {{Num(s.Xs)}}px 0 0 {{Num(s.Lg)}}px;
              padding: {{Num(s.Xs)}}px {{Num(s.Sm)}}px;
              background: var(--sunken);
              border-left: 3px solid var(--success);
            }
            .answer-label { font-size: {{Num(t.Caption)}}px; font-weight: {{t.WeightBold}}; color: var(--text-2); }

            .print-bar {
              position: sticky; top: 0; z-index: 1;
              display: flex; justify-content: center; gap: {{Num(s.Xs)}}px;
              padding: {{Num(s.Xs)}}px;
              background: var(--surface);
              border-bottom: 1px solid var(--border);
            }
            .print-bar button {
              font: inherit; font-size: {{Num(t.Caption)}}px;
              padding: {{Num(s.Xxs)}}px {{Num(s.Md)}}px;
              cursor: pointer;
              background: var(--primary); color: #fff;
              border: 0; border-radius: var(--radius);
            }
            .print-hint { color: var(--text-2); font-size: {{Num(t.Caption)}}px; align-self: center; }

            @media print {
              /* The browser's print engine does the pagination. These rules are
                 what stop it splitting a question across a page boundary, which
                 is the single thing that makes a printed paper look broken. */
              body { background: #fff; }
              .paper { max-width: none; margin: 0; padding: 0; border: 0; }
              .question { break-inside: avoid; page-break-inside: avoid; }
              .section-head { break-after: avoid; page-break-after: avoid; }
              .no-print { display: none !important; }
              a, a:visited { color: inherit; text-decoration: none; }
            }
            """;
    }

    // --- Sections -----------------------------------------------------------

    private static void AppendPrintBar(StringBuilder sb, HtmlExportOptions options)
    {
        if (!options.IncludePrintButton) return;

        sb.AppendLine("<div class=\"print-bar no-print\">");
        sb.AppendLine("  <button type=\"button\" onclick=\"window.print()\">Print or save as PDF</button>");
        sb.AppendLine("  <span class=\"print-hint\">Choose \"Save as PDF\" as the destination.</span>");
        sb.AppendLine("</div>");
    }

    private static void AppendHeader(StringBuilder sb, CompiledQuiz quiz, HtmlExportOptions options)
    {
        sb.AppendLine("<header>");
        sb.AppendLine($"  <h1>{Escape(quiz.Title)}</h1>");

        if (!string.IsNullOrWhiteSpace(quiz.Description))
            AppendDescription(sb, quiz.Description);

        var meta = new List<string>
        {
            quiz.QuestionCount == 1 ? "1 question" : $"{quiz.QuestionCount} questions",
            quiz.TotalPoints == 1 ? "1 point" : $"{Num(quiz.TotalPoints)} points",
        };

        if (quiz.TimeLimitMinutes is { } minutes)
            meta.Add($"{minutes} minutes");

        sb.AppendLine($"  <p class=\"meta\">{Escape(string.Join("  ·  ", meta))}</p>");

        var passLine = PassMarkLine(quiz);
        if (passLine is not null)
            sb.AppendLine($"  <p class=\"meta\">{Escape(passLine)}</p>");

        if (options.ShowAnswers)
            sb.AppendLine("  <p class=\"meta\">Answer key</p>");

        sb.AppendLine("</header>");
        sb.AppendLine("<hr class=\"rule\">");
    }

    /// <summary>
    /// Phrased in whichever unit the author chose. A bare "75%" is ambiguous on
    /// a weighted paper: 75% of the questions and 75% of the marks are
    /// different bars.
    /// </summary>
    private static string? PassMarkLine(CompiledQuiz quiz)
    {
        if (quiz.PassMarkBasis == PassMarkBasis.QuestionCount)
        {
            if (quiz.GradeableQuestionCount <= 0) return null;

            var label = quiz.QuestionsToPass == 1 ? "question" : "questions";
            return $"Pass mark: {quiz.PassPercentage}% of the questions "
                   + $"({quiz.QuestionsToPass} of {quiz.GradeableQuestionCount} {label} correct)";
        }

        if (quiz.TotalPoints <= 0) return null;

        return $"Pass mark: {quiz.PassPercentage}% of the points "
               + $"({Num(quiz.PointsToPass)} of {Num(quiz.TotalPoints)})";
    }

    private static void AppendSection(StringBuilder sb, CompiledSection section, HtmlExportOptions options)
    {
        sb.AppendLine("<section class=\"section\">");
        sb.AppendLine("  <div class=\"section-head\">");
        sb.AppendLine($"    <h2>{Escape(section.Title)}</h2>");

        var points = section.TotalPoints == 1 ? "1 point" : $"{Num(section.TotalPoints)} points";
        sb.AppendLine($"    <span class=\"section-points\">{Escape(points)}</span>");
        sb.AppendLine("  </div>");

        if (section.Questions.Count == 0)
        {
            sb.AppendLine("  <p class=\"empty-section\">(no questions in this section)</p>");
            sb.AppendLine("</section>");
            return;
        }

        foreach (var question in section.Questions)
            AppendQuestion(sb, question, options);

        sb.AppendLine("</section>");
    }

    private static void AppendQuestion(StringBuilder sb, CompiledQuestion compiled, HtmlExportOptions options)
    {
        var q = compiled.Question;

        sb.AppendLine("  <div class=\"question\">");
        sb.AppendLine("    <div class=\"question-head\">");
        sb.AppendLine($"      <span class=\"number\">{compiled.Number}.</span>");
        sb.AppendLine($"      <span class=\"prompt\">{Escape(q.Prompt)}</span>");

        var points = q.Points == 1 ? "1 point" : $"{Num(q.Points)} points";
        sb.AppendLine($"      <span class=\"points\">{Escape(points)}</span>");
        sb.AppendLine("    </div>");

        AppendImage(sb, q.ImageRelativePath, options.ImageDataUriResolver);

        AppendQuestionBody(sb, compiled, options);

        if (!string.IsNullOrWhiteSpace(q.Hint))
            sb.AppendLine($"    <p class=\"hint\">Hint: {Escape(q.Hint)}</p>");

        if (options.ShowAnswers)
        {
            sb.AppendLine("    <div class=\"answer\">");
            sb.AppendLine("      <div class=\"answer-label\">Answer</div>");
            sb.AppendLine($"      <div>{Escape(AnswerText(compiled))}</div>");
            sb.AppendLine("    </div>");
        }

        sb.AppendLine("  </div>");
    }

    private static void AppendImage(StringBuilder sb, string? imagePath, Func<string?, string?>? resolver)
    {
        if (string.IsNullOrEmpty(imagePath) || resolver is null) return;

        var dataUri = resolver(imagePath);
        if (string.IsNullOrEmpty(dataUri)) return;

        // alt left empty on purpose: the prompt already carries the meaning, and
        // an author-supplied filename would be noise for a screen reader.
        sb.AppendLine($"    <img class=\"question-image\" src=\"{dataUri}\" alt=\"\">");
    }

    private static void AppendQuestionBody(StringBuilder sb, CompiledQuestion compiled, HtmlExportOptions options)
    {
        switch (compiled.Question)
        {
            case MultipleChoiceSingleQuestion q:
                AppendOptions(sb, q.Choices.Select(c => (c.Text, c.IsCorrect)), options);
                break;

            case MultipleChoiceMultipleQuestion q:
                AppendOptions(sb, q.Choices.Select(c => (c.Text, c.IsCorrect)), options);
                break;

            case TrueFalseQuestion q:
                AppendOptions(sb, new[] { ("True", q.CorrectAnswer), ("False", !q.CorrectAnswer) }, options);
                break;

            case MatchingQuestion q:
                AppendMatching(sb, q, compiled.MatchingOptions, options);
                break;

            case SequenceQuestion q:
                AppendSequence(sb, q, compiled.SequencePresentation);
                break;

            case ShortAnswerQuestion:
                AppendLines(sb, 1);
                break;

            case EssayQuestion q:
                // Roughly ten words a line, floor of three so a short essay
                // still looks like an essay. Capped so a 5000-word suggestion
                // does not emit 500 ruled lines.
                AppendLines(sb, Math.Clamp(q.SuggestedWordCount / 10, 3, 40));
                break;

            case FillInTheBlankQuestion:
                // The blanks are already in the prompt as {{n}} tokens, so the
                // prompt above is the whole question.
                break;
        }
    }

    private static void AppendOptions(
        StringBuilder sb, IEnumerable<(string Text, bool IsCorrect)> choices, HtmlExportOptions options)
    {
        sb.AppendLine("    <ul class=\"options\">");

        var index = 0;
        foreach (var (text, isCorrect) in choices)
        {
            var mark = options.ShowAnswers && isCorrect
                ? "<span class=\"correct-mark\">&#10003;</span> "
                : string.Empty;

            sb.AppendLine("      <li class=\"option\">");
            sb.AppendLine($"        <span class=\"option-label\">{mark}{Letter(index)}.</span>");
            sb.AppendLine($"        <span>{Escape(text)}</span>");
            sb.AppendLine("      </li>");

            index++;
        }

        sb.AppendLine("    </ul>");
    }

    private static void AppendMatching(
        StringBuilder sb, MatchingQuestion q, IReadOnlyList<string>? rightColumn, HtmlExportOptions options)
    {
        sb.AppendLine("    <div class=\"options\">");

        foreach (var pair in q.Pairs.Where(p => !string.IsNullOrWhiteSpace(p.Left)))
        {
            sb.AppendLine("      <div class=\"match-row\">");
            sb.AppendLine("        <span class=\"match-blank\">&nbsp;</span>");
            sb.AppendLine($"        <span>{Escape(pair.Left)}</span>");
            sb.AppendLine("      </div>");
        }

        sb.AppendLine("    </div>");

        // Pre-shuffled by the compiler, so this cannot disagree with what the
        // Preview tab showed.
        if (rightColumn is { Count: > 0 })
        {
            sb.AppendLine("    <ul class=\"options\">");

            var index = 0;
            foreach (var option in rightColumn)
            {
                sb.AppendLine("      <li class=\"option\">");
                sb.AppendLine($"        <span class=\"option-label\">{Letter(index)}.</span>");
                sb.AppendLine($"        <span>{Escape(option)}</span>");
                sb.AppendLine("      </li>");
                index++;
            }

            sb.AppendLine("    </ul>");
        }
    }

    /// <summary>
    /// A sequence question on paper: the items are listed in their presentation
    /// order (pre-shuffled by the compiler, so this cannot disagree with the
    /// Preview tab), each with a blank for the taker to write the position it
    /// belongs in.
    /// </summary>
    private static void AppendSequence(
        StringBuilder sb, SequenceQuestion q, IReadOnlyList<int>? presentation)
    {
        // Fall back to authored order if the compiler gave no presentation
        // (only happens for a degenerate question with fewer than two items).
        var order = presentation is { Count: > 0 }
            ? presentation
            : Enumerable.Range(0, q.Items.Count).ToList();

        sb.AppendLine("    <div class=\"options\">");

        foreach (var sourceIndex in order)
        {
            if (sourceIndex < 0 || sourceIndex >= q.Items.Count) continue;

            sb.AppendLine("      <div class=\"match-row\">");
            sb.AppendLine("        <span class=\"match-blank\">&nbsp;</span>");
            sb.AppendLine($"        <span>{Escape(q.Items[sourceIndex])}</span>");
            sb.AppendLine("      </div>");
        }

        sb.AppendLine("    </div>");
    }

    private static void AppendLines(StringBuilder sb, int count)
    {
        if (count <= 0) return;

        sb.AppendLine("    <div class=\"lines\">");
        for (var i = 0; i < count; i++)
            sb.AppendLine("      <div class=\"line\"></div>");
        sb.AppendLine("    </div>");
    }

    private static string AnswerText(CompiledQuestion compiled) => compiled.Question switch
    {
        MultipleChoiceSingleQuestion q =>
            q.Choices.FirstOrDefault(c => c.IsCorrect)?.Text ?? "(no correct answer marked)",

        MultipleChoiceMultipleQuestion q =>
            q.Choices.Any(c => c.IsCorrect)
                ? string.Join(", ", q.Choices.Where(c => c.IsCorrect).Select(c => c.Text))
                : "(no correct answer marked)",

        TrueFalseQuestion q => q.CorrectAnswer ? "True" : "False",

        ShortAnswerQuestion q =>
            q.AcceptedAnswers.Count > 0 ? string.Join("  /  ", q.AcceptedAnswers) : "(no accepted answer)",

        FillInTheBlankQuestion q =>
            q.Blanks.Count > 0
                ? string.Join("   ", q.Blanks.Select(b =>
                    $"{{{{{b.Ordinal}}}}} = {(b.AcceptedAnswers.Count > 0 ? string.Join(" / ", b.AcceptedAnswers) : "(none)")}"))
                : "(no blanks)",

        MatchingQuestion q => string.Join("   ", q.Pairs.Select(p => $"{p.Left} -> {p.Right}")),

        // Items are stored in correct order, so the answer key is simply that
        // order joined with arrows.
        SequenceQuestion q => string.Join(" -> ", q.Items),

        EssayQuestion q => string.IsNullOrWhiteSpace(q.RubricNotes) ? "(graded by hand)" : q.RubricNotes,

        _ => string.Empty,
    };

    private static string Letter(int index) =>
        index < 26
            ? ((char)('A' + index)).ToString()
            // Past Z, keep numbering rather than wrapping back to 'A', which
            // would print two options with the same label.
            : (index + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Every piece of quiz text goes through this. A prompt reading
    /// "If x &lt; 5" would otherwise silently swallow the rest of the line, and
    /// a prompt containing a script tag would do rather more than that.
    ///
    /// Text that is ALREADY an entity is escaped again on purpose: someone who
    /// typed the characters &amp;amp; wants to see &amp;amp; on the page.
    /// Trying to detect "already escaped" input is how injection holes are born.
    /// </summary>
    /// <summary>
    /// Writes the description honouring the safelist: bold, italic, line breaks
    /// and bullet lists. Every run of text still goes through Escape, so the
    /// safelist adds formatting without opening the injection hole that raw HTML
    /// would -- the parser has already decided what is markup, and it is only
    /// ever these few tags.
    /// </summary>
    private static void AppendDescription(StringBuilder sb, string description)
    {
        foreach (var block in DescriptionParser.Parse(description))
        {
            switch (block)
            {
                case DescriptionParagraph paragraph:
                    sb.AppendLine($"  <p class=\"description\">{Runs(paragraph.Runs)}</p>");
                    break;

                case DescriptionList list:
                    sb.AppendLine("  <ul class=\"description\">");

                    foreach (var item in list.Items)
                        sb.AppendLine($"    <li>{Runs(item)}</li>");

                    sb.AppendLine("  </ul>");
                    break;
            }
        }
    }

    private static string Runs(IReadOnlyList<DescriptionRun> runs)
    {
        var sb = new StringBuilder();

        foreach (var run in runs)
        {
            if (run.IsLineBreak)
            {
                sb.Append("<br>");
                continue;
            }

            var text = Escape(run.Text);

            if (run.Bold) text = $"<strong>{text}</strong>";
            if (run.Italic) text = $"<em>{text}</em>";

            sb.Append(text);
        }

        return sb.ToString();
    }

    private static string Escape(string? text)
        => string.IsNullOrEmpty(text) ? string.Empty : WebUtility.HtmlEncode(text);
}
