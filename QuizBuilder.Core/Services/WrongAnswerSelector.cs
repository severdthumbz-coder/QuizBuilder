using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Picks the questions to drill for "practice only my wrong answers".
///
/// The rule (decided, and proven in tools/port/wrong_answers_stats_port.py):
/// for each live question, take its MOST RECENT verdict across the given
/// attempts and keep the question only if that verdict is wrong. A question you
/// later got right drops out; a question wrong on your latest sitting stays; a
/// question whose most recent appearance was NOT auto-gradeable (an essay or a
/// needs-review answer, <see cref="AttemptQuestionRecord.IsCorrect"/> null) is
/// excluded, because we cannot assert it was wrong.
///
/// Matching a stored record back to a live question prefers the record's
/// <see cref="AttemptQuestionRecord.QuestionId"/>; records written before that
/// field existed carry an empty id and fall back to a prompt-text match. "Most
/// recent" is by <see cref="AttemptRecord.TakenAt"/>, never list order.
///
/// Returns the live <see cref="Question"/> objects in quiz order, so a practice
/// paper compiled from them reads the way the quiz does.
/// </summary>
public static class WrongAnswerSelector
{
    public static IReadOnlyList<Question> SelectWrongQuestions(
        QuizDocument quiz,
        IEnumerable<AttemptRecord> attempts)
    {
        ArgumentNullException.ThrowIfNull(quiz);
        ArgumentNullException.ThrowIfNull(attempts);

        // Live questions in quiz (display) order, so the output order is stable
        // and matches what the author sees.
        var liveInOrder = quiz.SectionsInDisplayOrder()
            .SelectMany(s => s.Questions)
            .ToList();

        if (liveInOrder.Count == 0) return Array.Empty<Question>();

        var liveById = new Dictionary<Guid, Question>();
        foreach (var q in liveInOrder)
            liveById.TryAdd(q.Id, q);

        // First-add wins on a duplicated prompt (degenerate), mirroring the port.
        var liveIdByPrompt = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var q in liveInOrder)
            if (!string.IsNullOrEmpty(q.Prompt))
                liveIdByPrompt.TryAdd(q.Prompt, q.Id);

        Guid? Resolve(AttemptQuestionRecord rec)
        {
            if (rec.QuestionId != Guid.Empty && liveById.ContainsKey(rec.QuestionId))
                return rec.QuestionId;

            return liveIdByPrompt.TryGetValue(rec.Prompt, out var id) ? id : null;
        }

        // Most-recent verdict per live question id.
        var latestSeen = new Dictionary<Guid, DateTimeOffset>();
        var latestVerdict = new Dictionary<Guid, bool?>();

        // Order by TakenAt so ties resolve deterministically to the later one;
        // the timestamp guard also makes unsorted input correct.
        foreach (var attempt in attempts.OrderBy(a => a.TakenAt))
        {
            foreach (var rec in attempt.Questions)
            {
                var resolved = Resolve(rec);
                if (resolved is not { } qid) continue;

                if (!latestSeen.TryGetValue(qid, out var prev) || attempt.TakenAt >= prev)
                {
                    latestSeen[qid] = attempt.TakenAt;
                    latestVerdict[qid] = rec.IsCorrect;
                }
            }
        }

        var wrongIds = latestVerdict
            .Where(kv => kv.Value == false)   // strictly wrong; null (not graded) excluded
            .Select(kv => kv.Key)
            .ToHashSet();

        return liveInOrder.Where(q => wrongIds.Contains(q.Id)).ToList();
    }
}
