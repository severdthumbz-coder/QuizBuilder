using System.Text;
using System.Text.Json;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Theming;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Renders a quiz as a single self-grading HTML file.
///
/// The grading rules are duplicated here in JavaScript, because a static page
/// has no server to call the C# grader. That duplication is the real risk: if
/// the two graders disagree, the same quiz scores differently in the app and the
/// browser. Both were ported to a reference model and checked to agree on a
/// battery covering every rule, the essay exclusion, and the pass boundary; the
/// embedded script mirrors that model line for line, and it self-tests in the
/// console on load so a future regression is visible.
/// </summary>
public sealed class QuizWebExporter : IQuizWebExporter
{
    // Default encoder: escapes < > & to \u003c etc, which is exactly what makes
    // the embedded JSON safe inside a <script> tag. A "</script>" in any string
    // becomes "\u003c/script\u003e" and cannot close the block early.
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
    };

    public string Render(CompiledQuiz quiz, ThemeTokens theme, WebExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(quiz);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(options);

        var model = BuildModel(quiz);
        var modelJson = JsonSerializer.Serialize(model, Json);
        var optsJson = JsonSerializer.Serialize(new
        {
            passPercentage = options.PassPercentage,
            passOnQuestionCount = options.PassOnQuestionCount,
            timeLimitMinutes = options.TimeLimitMinutes,
        }, Json);

        var sb = new StringBuilder();

        sb.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n");
        sb.Append("<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append($"<title>{Escape(quiz.Title)}</title>\n");
        sb.Append("<style>\n").Append(Css(theme)).Append("\n</style>\n");
        sb.Append("</head>\n<body>\n");

        sb.Append("<main>\n");
        sb.Append($"<h1>{Escape(quiz.Title)}</h1>\n");

        if (!string.IsNullOrWhiteSpace(quiz.Description))
            AppendDescription(sb, quiz.Description);

        sb.Append("<p class=\"note\">This quiz marks itself in your browser. It is for "
                  + "self-assessment &mdash; the answers are stored in this page, so treat it as a "
                  + "practice tool, not a supervised exam.</p>\n");

        sb.Append("<div id=\"timer\" class=\"timer\" hidden></div>\n");
        sb.Append("<form id=\"quiz\" onsubmit=\"return false;\">\n");

        foreach (var section in quiz.Sections)
        {
            if (quiz.Sections.Count > 1)
                sb.Append($"<h2>{Escape(section.Title)}</h2>\n");

            foreach (var compiled in section.Questions)
                AppendQuestion(sb, compiled, options.ImageDataUriResolver);
        }

        sb.Append("</form>\n");

        sb.Append("<div class=\"actions\">\n");
        sb.Append("<button type=\"button\" id=\"submit\" onclick=\"submitQuiz()\">Submit answers</button>\n");
        sb.Append("</div>\n");

        sb.Append("<section id=\"results\" class=\"results\" hidden></section>\n");

        sb.Append("</main>\n");

        sb.Append("<script>\n");
        sb.Append("const QUIZ = ").Append(modelJson).Append(";\n");
        sb.Append("const OPTS = ").Append(optsJson).Append(";\n");
        sb.Append(GraderScript());
        sb.Append("\n</script>\n");

        sb.Append("</body>\n</html>\n");

        return sb.ToString();
    }

    // --- Model --------------------------------------------------------------

    /// <summary>
    /// The quiz as a plain object graph for JSON. Only what the grader needs and
    /// what the inputs render -- notably the answer key, which has to be here for
    /// client-side grading to be possible at all.
    /// </summary>
    private static object BuildModel(CompiledQuiz quiz)
    {
        var questions = new List<object>();

        foreach (var compiled in quiz.Sections.SelectMany(s => s.Questions))
        {
            var q = compiled.Question;

            switch (q)
            {
                case MultipleChoiceSingleQuestion mc:
                    questions.Add(new
                    {
                        n = compiled.Number,
                        type = "single",
                        points = q.Points,
                        prompt = q.Prompt,
                        choices = mc.Choices.Select(c => new { text = c.Text, correct = c.IsCorrect }).ToArray(),
                    });
                    break;

                case MultipleChoiceMultipleQuestion mm:
                    questions.Add(new
                    {
                        n = compiled.Number,
                        type = "multiple",
                        points = q.Points,
                        prompt = q.Prompt,
                        partial = mm.AllowPartialCredit,
                        choices = mm.Choices.Select(c => new { text = c.Text, correct = c.IsCorrect }).ToArray(),
                    });
                    break;

                case TrueFalseQuestion tf:
                    questions.Add(new
                    {
                        n = compiled.Number,
                        type = "truefalse",
                        points = q.Points,
                        prompt = q.Prompt,
                        correct = tf.CorrectAnswer,
                    });
                    break;

                case ShortAnswerQuestion sa:
                    questions.Add(new
                    {
                        n = compiled.Number,
                        type = "short",
                        points = q.Points,
                        prompt = q.Prompt,
                        accepted = sa.AcceptedAnswers.ToArray(),
                        caseSensitive = sa.CaseSensitive,
                    });
                    break;

                case FillInTheBlankQuestion fb:
                    questions.Add(new
                    {
                        n = compiled.Number,
                        type = "blanks",
                        points = q.Points,
                        prompt = q.Prompt,
                        caseSensitive = fb.CaseSensitive,

                        // Ordinal order, so index i in the browser matches index i
                        // in the C# grader, which also orders by ordinal.
                        blanks = fb.Blanks
                            .OrderBy(b => b.Ordinal)
                            .Select(b => new { accepted = b.AcceptedAnswers.ToArray() })
                            .ToArray(),
                    });
                    break;

                case MatchingQuestion mq:
                    questions.Add(new
                    {
                        n = compiled.Number,
                        type = "matching",
                        points = q.Points,
                        prompt = q.Prompt,
                        pairs = mq.Pairs.Select(p => new { left = p.Left, right = p.Right }).ToArray(),

                        // The shuffled right-hand column the taker chooses from,
                        // pre-computed in compilation so it matches every surface.
                        options = compiled.MatchingOptions?.ToArray() ?? Array.Empty<string>(),
                    });
                    break;

                case SequenceQuestion sq:
                    // Items are emitted in the compiler's presentation order,
                    // each carrying its authored index. The taker drags the
                    // items around; on submit the JS reads the authored index
                    // out of each slot, so the grader compares against the same
                    // 0..n-1 answer key ScoreSequence uses. n is the number of
                    // items, so the grader can range-check without the key.
                    var presentation = compiled.SequencePresentation
                        ?? Enumerable.Range(0, sq.Items.Count).ToList();

                    questions.Add(new
                    {
                        n = compiled.Number,
                        type = "sequence",
                        points = q.Points,
                        prompt = q.Prompt,
                        count = sq.Items.Count,
                        items = presentation
                            .Where(i => i >= 0 && i < sq.Items.Count)
                            .Select(i => new { index = i, text = sq.Items[i] })
                            .ToArray(),
                    });
                    break;

                case EssayQuestion:
                    questions.Add(new
                    {
                        n = compiled.Number,
                        type = "essay",
                        points = q.Points,
                        prompt = q.Prompt,
                    });
                    break;
            }
        }

        return new { title = quiz.Title, questions };
    }

    // --- Question inputs ----------------------------------------------------

    private static void AppendQuestion(StringBuilder sb, CompiledQuestion compiled, Func<string?, string?>? imageResolver)
    {
        var q = compiled.Question;
        var n = compiled.Number;
        var points = q.Points == 1 ? "1 point" : $"{Num(q.Points)} points";

        sb.Append($"<div class=\"q\" data-n=\"{n}\" data-type=\"{TypeOf(q)}\">\n");
        sb.Append("<div class=\"q-head\">");
        sb.Append($"<span class=\"q-num\">{n}.</span> <span class=\"q-prompt\">{Escape(q.Prompt)}</span>");
        sb.Append($"<span class=\"q-points\">{points}</span>");
        sb.Append("</div>\n");

        AppendImage(sb, q.ImageRelativePath, imageResolver);

        switch (q)
        {
            case MultipleChoiceSingleQuestion mc:
                for (var i = 0; i < mc.Choices.Count; i++)
                    sb.Append($"<label class=\"opt\"><input type=\"radio\" name=\"q{n}\" value=\"{i}\"> {Escape(mc.Choices[i].Text)}</label>\n");
                break;

            case MultipleChoiceMultipleQuestion mm:
                sb.Append("<div class=\"hint\">Select all that apply.</div>\n");
                for (var i = 0; i < mm.Choices.Count; i++)
                    sb.Append($"<label class=\"opt\"><input type=\"checkbox\" name=\"q{n}\" value=\"{i}\"> {Escape(mm.Choices[i].Text)}</label>\n");
                break;

            case TrueFalseQuestion:
                sb.Append($"<label class=\"opt\"><input type=\"radio\" name=\"q{n}\" value=\"true\"> True</label>\n");
                sb.Append($"<label class=\"opt\"><input type=\"radio\" name=\"q{n}\" value=\"false\"> False</label>\n");
                break;

            case ShortAnswerQuestion:
                sb.Append($"<input type=\"text\" class=\"short\" name=\"q{n}\" autocomplete=\"off\">\n");
                break;

            case FillInTheBlankQuestion fb:
                var ordered = fb.Blanks.OrderBy(b => b.Ordinal).ToList();
                for (var i = 0; i < ordered.Count; i++)
                    sb.Append($"<div class=\"blank\"><span class=\"blank-n\">{i + 1}.</span>"
                              + $"<input type=\"text\" class=\"short\" name=\"q{n}_b{i}\" autocomplete=\"off\"></div>\n");
                break;

            case MatchingQuestion mq:
                var opts = compiled.MatchingOptions ?? new List<string>();
                for (var i = 0; i < mq.Pairs.Count; i++)
                {
                    sb.Append("<div class=\"match\">");
                    sb.Append($"<span class=\"match-left\">{Escape(mq.Pairs[i].Left)}</span>");
                    sb.Append($"<select name=\"q{n}_m{i}\"><option value=\"\"></option>");
                    foreach (var opt in opts)
                        sb.Append($"<option value=\"{Escape(opt)}\">{Escape(opt)}</option>");
                    sb.Append("</select></div>\n");
                }

                break;

            case SequenceQuestion sq:
                var order = compiled.SequencePresentation
                    ?? Enumerable.Range(0, sq.Items.Count).ToList();

                sb.Append("<div class=\"hint\">Drag the items into the correct order, top to bottom.</div>\n");
                sb.Append($"<ol class=\"seq\" data-q=\"{n}\">\n");
                foreach (var sourceIndex in order)
                {
                    if (sourceIndex < 0 || sourceIndex >= sq.Items.Count) continue;

                    // data-index is the AUTHORED index (the answer key domain).
                    // The taker rearranges the <li>s; on submit collect() reads
                    // data-index top-to-bottom, giving exactly the SequenceAnswer
                    // the grader expects.
                    sb.Append($"<li class=\"seq-item\" draggable=\"true\" data-index=\"{sourceIndex}\">"
                              + $"<span class=\"seq-grip\" aria-hidden=\"true\">&#x2807;</span>"
                              + $"<span class=\"seq-text\">{Escape(sq.Items[sourceIndex])}</span></li>\n");
                }
                sb.Append("</ol>\n");
                break;

            case EssayQuestion:
                sb.Append("<div class=\"hint\">This answer is not marked automatically &mdash; it will be listed for your review.</div>\n");
                sb.Append($"<textarea name=\"q{n}\" rows=\"5\"></textarea>\n");
                break;
        }

        sb.Append("</div>\n");
    }

    private static void AppendImage(StringBuilder sb, string? imagePath, Func<string?, string?>? resolver)
    {
        if (string.IsNullOrEmpty(imagePath) || resolver is null) return;

        var dataUri = resolver(imagePath);
        if (string.IsNullOrEmpty(dataUri)) return;

        sb.Append($"<img class=\"q-image\" src=\"{dataUri}\" alt=\"\">\n");
    }

    private static string TypeOf(Question q) => q switch
    {
        MultipleChoiceSingleQuestion => "single",
        MultipleChoiceMultipleQuestion => "multiple",
        TrueFalseQuestion => "truefalse",
        ShortAnswerQuestion => "short",
        FillInTheBlankQuestion => "blanks",
        MatchingQuestion => "matching",
        SequenceQuestion => "sequence",
        EssayQuestion => "essay",
        _ => "unknown",
    };

    private static void AppendDescription(StringBuilder sb, string description)
    {
        foreach (var block in DescriptionParser.Parse(description))
        {
            switch (block)
            {
                case DescriptionParagraph p:
                    sb.Append("<p class=\"description\">").Append(Runs(p.Runs)).Append("</p>\n");
                    break;

                case DescriptionList list:
                    sb.Append("<ul class=\"description\">");
                    foreach (var item in list.Items)
                        sb.Append("<li>").Append(Runs(item)).Append("</li>");
                    sb.Append("</ul>\n");
                    break;
            }
        }
    }

    private static string Runs(IReadOnlyList<DescriptionRun> runs)
    {
        var sb = new StringBuilder();

        foreach (var run in runs)
        {
            if (run.IsLineBreak) { sb.Append("<br>"); continue; }

            var text = Escape(run.Text);
            if (run.Bold) text = $"<strong>{text}</strong>";
            if (run.Italic) text = $"<em>{text}</em>";
            sb.Append(text);
        }

        return sb.ToString();
    }

    // --- CSS ----------------------------------------------------------------

    private static string Css(ThemeTokens theme)
    {
        var c = theme.Colors;

        // Kept deliberately small and self-contained. The printable HTML export
        // has the elaborate token-driven stylesheet; a self-grading practice
        // page wants to be readable and obvious, not typeset.
        return $$"""
            :root {
              --bg: {{Hex(c.Background)}};
              --surface: {{Hex(c.Surface)}};
              --text: {{Hex(c.TextPrimary)}};
              --text-2: {{Hex(c.TextSecondary)}};
              --border: {{Hex(c.Border)}};
              --primary: {{Hex(c.Primary)}};
              --on-primary: {{Hex(c.OnPrimary)}};
              --ok: {{Hex(c.Success)}};
              --bad: {{Hex(c.Error)}};
            }
            * { box-sizing: border-box; }
            body { margin: 0; background: var(--bg); color: var(--text);
                   font-family: -apple-system, Segoe UI, Roboto, sans-serif; line-height: 1.5; }
            main { max-width: 760px; margin: 0 auto; padding: 32px 20px 64px; }
            h1 { margin: 0 0 8px; }
            h2 { margin: 32px 0 8px; font-size: 1.15rem; }
            .description { color: var(--text-2); margin: 4px 0; }
            ul.description { padding-left: 20px; }
            .note { background: var(--surface); border: 1px solid var(--border);
                    border-radius: 8px; padding: 12px 16px; color: var(--text-2);
                    font-size: .9rem; margin: 16px 0 24px; }
            .q { background: var(--surface); border: 1px solid var(--border);
                 border-radius: 10px; padding: 16px; margin: 0 0 12px; }
            .q-head { margin-bottom: 8px; }
            .q-num { font-weight: 700; }
            .q-points { color: var(--text-2); font-size: .85rem; float: right; }
            .hint { color: var(--text-2); font-size: .85rem; margin: 4px 0; }
            .q-image { display: block; max-width: 100%; height: auto; margin: 8px 0; border-radius: 6px; }
            .opt { display: block; padding: 4px 0; cursor: pointer; }
            .short { width: 100%; max-width: 360px; padding: 8px; border: 1px solid var(--border);
                     border-radius: 6px; background: var(--bg); color: var(--text); }
            .blank { display: flex; align-items: center; gap: 8px; margin: 4px 0; }
            .blank-n { width: 24px; color: var(--text-2); }
            .match { display: flex; align-items: center; gap: 12px; margin: 4px 0; }
            .match-left { min-width: 160px; }
            .match select { padding: 6px; border: 1px solid var(--border); border-radius: 6px;
                            background: var(--bg); color: var(--text); }
            .seq { list-style: none; padding: 0; margin: 8px 0; max-width: 480px; }
            .seq-item { display: flex; align-items: center; gap: 10px; padding: 10px 12px;
                        margin: 6px 0; border: 1px solid var(--border); border-radius: 8px;
                        background: var(--bg); color: var(--text); cursor: grab;
                        touch-action: none; user-select: none; }
            .seq-item.dragging { opacity: .5; cursor: grabbing; }
            .seq-grip { color: var(--text-2); font-size: 1.1rem; line-height: 1; }
            .seq-text { flex: 1; }
            textarea { width: 100%; padding: 8px; border: 1px solid var(--border);
                       border-radius: 6px; background: var(--bg); color: var(--text);
                       font-family: inherit; }
            .timer { position: sticky; top: 0; z-index: 10; background: var(--surface);
                     border: 1px solid var(--border); border-radius: 8px; padding: 10px 16px;
                     margin: 0 0 16px; font-weight: 700; text-align: center; }
            .timer.low { color: var(--bad); border-color: var(--bad); }
            .actions { margin: 24px 0; }
            button { background: var(--primary); color: var(--on-primary); border: 0;
                     border-radius: 8px; padding: 12px 24px; font-size: 1rem; cursor: pointer; }
            .results { background: var(--surface); border: 1px solid var(--border);
                       border-radius: 10px; padding: 24px; margin-top: 24px; }
            .results h2 { margin-top: 0; }
            .headline { font-size: 1.5rem; font-weight: 700; }
            .headline.pass { color: var(--ok); }
            .headline.fail { color: var(--bad); }
            .score { font-size: 1.2rem; margin: 8px 0; }
            .detail, .review-line { color: var(--text-2); }
            .wrong { border-left: 3px solid var(--bad); padding-left: 12px; margin: 12px 0; }
            .review { border-left: 3px solid var(--border); padding-left: 12px; margin: 12px 0; }
            .wrong .label, .review .label { font-weight: 700; }
            """;
    }

    // --- The embedded grader ------------------------------------------------

    /// <summary>
    /// The JavaScript grader. A transcription of the C# QuizGrader and the
    /// reference model both were verified against -- same rules, same order,
    /// same floor-at-zero, same essay exclusion, same pass-from-percentage. It
    /// runs a self-test against a small battery on load and logs the result, so
    /// a regression surfaces in the console rather than silently.
    /// </summary>
    private static string GraderScript() => """
        function norm(s, cs) {
          s = (s == null ? "" : String(s)).trim();
          return cs ? s : s.toLowerCase();
        }
        function matches(given, accepted, cs) {
          const g = norm(given, cs);
          return accepted.some(a => g === norm(a, cs));
        }

        // Mirrors QuizGrader.Score: one rule per type, essay handled by caller.
        function scoreQuestion(q, ans) {
          const pts = q.points;
          switch (q.type) {
            case "single": {
              const i = ans.choiceIndex;
              if (i == null || i < 0 || i >= q.choices.length) return 0;
              return q.choices[i].correct ? pts : 0;
            }
            case "multiple": {
              const correct = q.choices.map((c, i) => [c, i]).filter(x => x[0].correct).map(x => x[1]);
              const picked = ans.choiceIndices || [];
              if (correct.length === 0) return 0;
              const cset = new Set(correct), pset = new Set(picked);
              if (!q.partial) {
                if (pset.size !== cset.size) return 0;
                for (const x of pset) if (!cset.has(x)) return 0;
                return pts;
              }
              let hits = 0, misses = 0;
              for (const p of pset) (cset.has(p) ? hits++ : misses++);
              const frac = (hits - misses) / correct.length;
              return Math.max(0, frac) * pts;
            }
            case "truefalse":
              return ans.bool === q.correct ? pts : 0;
            case "short":
              if (!ans.text || !ans.text.trim()) return 0;
              return matches(ans.text, q.accepted, q.caseSensitive) ? pts : 0;
            case "blanks": {
              if (q.blanks.length === 0) return 0;
              let hits = 0;
              for (let i = 0; i < q.blanks.length; i++) {
                const g = (ans.blanks || {})[i];
                if (!g || !g.trim()) continue;
                if (matches(g, q.blanks[i].accepted, q.caseSensitive)) hits++;
              }
              return hits / q.blanks.length * pts;
            }
            case "matching": {
              if (q.pairs.length === 0) return 0;
              let hits = 0;
              for (let i = 0; i < q.pairs.length; i++) {
                const g = (ans.match || {})[i];
                if (g != null && g === q.pairs[i].right) hits++;
              }
              return hits / q.pairs.length * pts;
            }
            case "sequence": {
              // Mirrors QuizGrader.ScoreSequence: adjacent-pairs partial credit
              // over a permutation of the authored indices 0..n-1. ans.order is
              // the taker's arrangement, top to bottom.
              const n = q.count;
              const given = ans.order || [];
              if (n < 2) return (n === 1 && given.length === 1 && given[0] === 0) ? pts : 0;
              if (given.length !== n) return 0;
              const seen = new Array(n).fill(false);
              for (const idx of given) {
                if (idx < 0 || idx >= n) return 0;
                if (seen[idx]) return 0;
                seen[idx] = true;
              }
              let hits = 0;
              for (let i = 0; i < n - 1; i++) if (given[i] + 1 === given[i + 1]) hits++;
              return hits / (n - 1) * pts;
            }
            default:
              return 0;
          }
        }

        // Mirrors QuizGrader.Grade: essays excluded from the denominator, pass
        // taken from the same percentage shown.
        function grade(quiz, answers, opts) {
          const results = quiz.questions.map((q, idx) => {
            const needsReview = q.type === "essay";
            const scored = needsReview ? 0 : scoreQuestion(q, answers[idx] || {});
            return { q, scored, possible: q.points, needsReview };
          });

          const auto = results.filter(r => !r.needsReview && r.possible > 0);
          const autoPoss = auto.reduce((s, r) => s + r.possible, 0);
          const autoScored = auto.reduce((s, r) => s + r.scored, 0);
          const review = results.filter(r => r.needsReview);

          let pct = null, passed = null;
          if (auto.length > 0 && autoPoss > 0) {
            if (opts.passOnQuestionCount) {
              const correct = auto.filter(r => r.possible > 0 && (r.scored / r.possible) >= 0.5).length;
              pct = correct / auto.length * 100;
            } else {
              pct = autoScored / autoPoss * 100;
            }
            passed = pct >= opts.passPercentage;
          }

          return {
            results, autoScored, autoPoss,
            reviewPoints: review.reduce((s, r) => s + r.possible, 0),
            reviewCount: review.length, pct, passed,
          };
        }

        // Read the DOM into the answer shape the grader expects.
        function collect() {
          const answers = {};
          QUIZ.questions.forEach((q, idx) => {
            const n = q.n;
            if (q.type === "single") {
              const el = document.querySelector(`input[name="q${n}"]:checked`);
              answers[idx] = { choiceIndex: el ? parseInt(el.value, 10) : null };
            } else if (q.type === "multiple") {
              const els = document.querySelectorAll(`input[name="q${n}"]:checked`);
              answers[idx] = { choiceIndices: Array.from(els).map(e => parseInt(e.value, 10)) };
            } else if (q.type === "truefalse") {
              const el = document.querySelector(`input[name="q${n}"]:checked`);
              answers[idx] = { bool: el ? el.value === "true" : null };
            } else if (q.type === "short") {
              const el = document.querySelector(`input[name="q${n}"]`);
              answers[idx] = { text: el ? el.value : "" };
            } else if (q.type === "blanks") {
              const b = {};
              q.blanks.forEach((_, i) => {
                const el = document.querySelector(`input[name="q${n}_b${i}"]`);
                if (el) b[i] = el.value;
              });
              answers[idx] = { blanks: b };
            } else if (q.type === "matching") {
              const m = {};
              q.pairs.forEach((_, i) => {
                const el = document.querySelector(`select[name="q${n}_m${i}"]`);
                if (el && el.value) m[i] = el.value;
              });
              answers[idx] = { match: m };
            } else if (q.type === "sequence") {
              // Read the authored index off each <li> in its current on-screen
              // order. That top-to-bottom list of indices is exactly the
              // SequenceAnswer the grader scores.
              const list = document.querySelector(`ol.seq[data-q="${n}"]`);
              const order = list
                ? Array.from(list.querySelectorAll("li.seq-item"))
                    .map(li => parseInt(li.getAttribute("data-index"), 10))
                : [];
              answers[idx] = { order };
            } else {
              answers[idx] = {};
            }
          });
          return answers;
        }

        function fmt(x) { return Number.isInteger(x) ? String(x) : x.toFixed(2).replace(/\.?0+$/, ""); }
        function esc(s) {
          return String(s).replace(/[&<>"']/g, c =>
            ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
        }

        function describeCorrect(q) {
          switch (q.type) {
            case "single": { const c = q.choices.find(x => x.correct); return c ? c.text : ""; }
            case "multiple": return q.choices.filter(c => c.correct).map(c => c.text).join(", ");
            case "truefalse": return q.correct ? "True" : "False";
            case "short": return q.accepted.join(" / ");
            case "blanks": return q.blanks.map((b, i) => `${i + 1}: ${b.accepted.join(" / ")}`).join(", ");
            case "matching": return q.pairs.map(p => `${p.left} \u2192 ${p.right}`).join(", ");
            case "sequence":
              // Items arrive in presentation order carrying their authored
              // index; sorting by that index reconstructs the correct order.
              return q.items.slice().sort((a, b) => a.index - b.index)
                .map(it => it.text).join(" \u2192 ");
            default: return "";
          }
        }

        function submitQuiz() {
          const answers = collect();
          const r = grade(QUIZ, answers, OPTS);
          const el = document.getElementById("results");

          let html = "";
          if (r.pct == null) {
            html += `<div class="headline">Answers recorded</div>`;
            html += `<div class="score">This quiz has no questions that can be marked automatically.</div>`;
          } else {
            const pass = r.passed;
            html += `<div class="headline ${pass ? "pass" : "fail"}">${pass ? "Congratulations!" : "Not this time"}</div>`;
            html += `<div class="score">${r.pct.toFixed(1)}%</div>`;
            const qual = r.reviewCount > 0 ? " that could be marked automatically" : "";
            html += `<div class="detail">${fmt(r.autoScored)} of ${fmt(r.autoPoss)} points${qual}.</div>`;
          }

          if (r.reviewCount > 0) {
            const pl = r.reviewCount === 1 ? "question" : "questions";
            html += `<div class="review-line">${r.reviewCount} ${pl} (${fmt(r.reviewPoints)} points) need your review.</div>`;
          }

          const wrong = r.results.filter(x => !x.needsReview && x.possible > 0 && (x.scored / x.possible) < 0.5);
          if (wrong.length) {
            html += `<h2>Questions you got wrong</h2>`;
            for (const x of wrong)
              html += `<div class="wrong"><div><span class="label">${x.q.n}.</span> ${esc(x.q.prompt)}</div>`
                    + `<div class="detail">Answer: ${esc(describeCorrect(x.q))}</div></div>`;
          }

          const review = r.results.filter(x => x.needsReview);
          if (review.length) {
            html += `<h2>Needs your review</h2>`;
            for (const x of review)
              html += `<div class="review"><span class="label">${x.q.n}.</span> ${esc(x.q.prompt)}</div>`;
          }

          el.innerHTML = html;
          el.hidden = false;
          el.scrollIntoView({ behavior: "smooth" });
          document.getElementById("submit").disabled = true;
        }

        // Countdown timer. Mirrors the app: when the limit is reached the quiz
        // auto-submits, scoring unanswered questions zero -- so the limit means
        // the same thing in the browser as in the app, rather than being
        // silently absent. No limit set: the bar stays hidden and nothing runs.
        (function startTimer() {
          const minutes = OPTS.timeLimitMinutes;
          if (!minutes || minutes <= 0) return;

          const bar = document.getElementById("timer");
          bar.hidden = false;

          let remaining = Math.round(minutes * 60);
          let submitted = false;

          function render() {
            const m = Math.floor(remaining / 60);
            const s = remaining % 60;
            bar.textContent = `Time remaining: ${m}:${String(s).padStart(2, "0")}`;
            // Flag the last minute so it reads as urgent, matching the app's cue.
            bar.classList.toggle("low", remaining <= 60);
          }

          render();

          const handle = setInterval(function () {
            remaining -= 1;

            if (remaining <= 0) {
              remaining = 0;
              render();
              clearInterval(handle);

              // Guard against a double submit if the user also clicked Submit in
              // the same instant.
              if (!submitted && !document.getElementById("submit").disabled) {
                submitted = true;
                bar.textContent = "Time's up";
                submitQuiz();
              }
              return;
            }

            render();
          }, 1000);
        })();

        // Sequence drag-to-reorder. Uses the HTML5 drag events with a pointer
        // fallback so it works with a mouse and, via touch-action, on touch.
        // Only the order of the <li>s matters; collect() reads data-index off
        // them at submit time, so no state is kept here beyond the DOM order.
        (function initSequences() {
          const lists = document.querySelectorAll("ol.seq");
          lists.forEach(list => {
            let dragging = null;

            list.addEventListener("dragstart", e => {
              const li = e.target.closest("li.seq-item");
              if (!li) return;
              dragging = li;
              li.classList.add("dragging");
              e.dataTransfer.effectAllowed = "move";
              // Firefox needs data set for the drag to start.
              try { e.dataTransfer.setData("text/plain", ""); } catch (_) {}
            });

            list.addEventListener("dragend", () => {
              if (dragging) dragging.classList.remove("dragging");
              dragging = null;
            });

            list.addEventListener("dragover", e => {
              e.preventDefault();
              if (!dragging) return;
              const after = itemAfter(list, e.clientY);
              if (after == null) list.appendChild(dragging);
              else list.insertBefore(dragging, after);
            });

            // The item the cursor is currently above: the first whose vertical
            // midpoint is below the cursor. Null means past the last item.
            function itemAfter(container, y) {
              const items = Array.from(
                container.querySelectorAll("li.seq-item:not(.dragging)"));
              for (const item of items) {
                const box = item.getBoundingClientRect();
                if (y < box.top + box.height / 2) return item;
              }
              return null;
            }
          });
        })();

        // Self-test: a small battery that must agree with the app. Logs to the
        // console so a broken grader is visible without reading the source.
        (function selfTest() {
          const T = [
            [{ type: "single", points: 2, choices: [{ correct: false }, { correct: true }] }, { choiceIndex: 1 }, 2],
            [{ type: "multiple", points: 4, partial: true, choices: [{ correct: true }, { correct: true }, { correct: false }, { correct: false }] }, { choiceIndices: [0, 1, 2, 3] }, 0],
            [{ type: "multiple", points: 4, partial: true, choices: [{ correct: true }, { correct: true }, { correct: false }, { correct: false }] }, { choiceIndices: [0] }, 2],
            [{ type: "short", points: 2, accepted: ["Paris"], caseSensitive: false }, { text: "  paris " }, 2],
            [{ type: "blanks", points: 4, caseSensitive: false, blanks: [{ accepted: ["cat"] }, { accepted: ["black"] }] }, { blanks: { 0: "cat" } }, 2],
            [{ type: "matching", points: 3, pairs: [{ left: "1", right: "a" }, { left: "2", right: "b" }, { left: "3", right: "c" }] }, { match: { 0: "a", 1: "b" } }, 2],
            [{ type: "sequence", points: 3, count: 3 }, { order: [0, 1, 2] }, 3],
            [{ type: "sequence", points: 3, count: 5 }, { order: [1, 2, 3, 4, 0] }, 2.25],
            [{ type: "sequence", points: 3, count: 5 }, { order: [0, 2, 1] }, 0],
          ];
          let ok = 0;
          for (const [q, a, want] of T) {
            const got = scoreQuestion(q, a);
            if (Math.abs(got - want) < 1e-9) ok++;
            else console.error("Self-grading quiz: grader mismatch", { q, a, want, got });
          }
          console.log(`Self-grading quiz: grader self-test ${ok}/${T.length} passed`);
        })();
        """;

    // --- Shared helpers -----------------------------------------------------

    private static string Escape(string? text) => System.Net.WebUtility.HtmlEncode(text ?? string.Empty);

    private static string Hex(string cssColor)
    {
        // Theme tokens are #RRGGBB or #RRGGBBAA. Browsers accept both, so pass
        // through after a light sanity check -- never emit an unvalidated string
        // into a stylesheet.
        var t = (cssColor ?? string.Empty).Trim();

        if (t.StartsWith('#') && (t.Length == 7 || t.Length == 9)
            && t[1..].All(Uri.IsHexDigit))
        {
            return t;
        }

        return "#000000";
    }

    private static string Num(double value) =>
        value == Math.Floor(value)
            ? value.ToString("0", System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
