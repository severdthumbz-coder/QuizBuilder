using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;

namespace QuizBuilder.Player.Models;

/// <summary>
/// The live state of one sitting: the compiled paper, the answer being built
/// for each question, and a wall-clock start so the grader can be told how long
/// it took. Deliberately a plain state holder -- the scoring lives in Core's
/// QuizGrader, which this feeds.
/// </summary>
public sealed class TakeSession
{
    public required CompiledQuiz Quiz { get; init; }

    /// <summary>
    /// One <see cref="QuestionAnswer"/> per compiled question, keyed by the
    /// compiled question itself. The grader is keyed the same way (a compiled
    /// question's Id is freshly minted by Clone() and can't be matched back to
    /// the authored one), so this dictionary is handed straight to Grade().
    /// </summary>
    public Dictionary<CompiledQuestion, QuestionAnswer> Answers { get; } = new();

    /// <summary>All compiled questions in presentation order, flattened across sections.</summary>
    public IReadOnlyList<CompiledQuestion> Questions { get; init; } = Array.Empty<CompiledQuestion>();

    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;

    /// <summary>The answer object for a question, creating an empty one on first touch.</summary>
    public QuestionAnswer AnswerFor(CompiledQuestion question)
    {
        if (!Answers.TryGetValue(question, out var a))
        {
            a = new QuestionAnswer();
            Answers[question] = a;
        }
        return a;
    }
}
