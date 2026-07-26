using QuizBuilder.Core.Services;

namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// What someone answered for one question.
///
/// One type covers all eight question types rather than a polymorphic hierarchy:
/// the shapes barely overlap, and a class per type would mean a cast at every
/// use. Only the field matching the question's type is read, and the grader
/// decides which that is.
/// </summary>
public sealed class QuestionAnswer
{
    /// <summary>Multiple choice (single): the chosen index, or null.</summary>
    public int? ChoiceIndex { get; set; }

    /// <summary>Multiple choice (multiple): every chosen index.</summary>
    public HashSet<int> ChoiceIndices { get; set; } = new();

    /// <summary>True/false.</summary>
    public bool? BoolAnswer { get; set; }

    /// <summary>Short answer: the typed text.</summary>
    public string? TextAnswer { get; set; }

    /// <summary>Fill in the blank: text per blank, keyed by 0-based position.</summary>
    public Dictionary<int, string> BlankAnswers { get; set; } = new();

    /// <summary>Matching: the chosen right-hand value per left item, keyed by 0-based position.</summary>
    public Dictionary<int, string> MatchAnswers { get; set; } = new();

    /// <summary>Essay: the written response. Never auto-graded.</summary>
    public string? EssayAnswer { get; set; }

    /// <summary>
    /// Sequence: the authored item indices, in the order the taker arranged
    /// them. Indices rather than the item text, so two items reading the same
    /// ("Repeat", "Repeat") stay distinguishable.
    /// </summary>
    public List<int> SequenceAnswer { get; set; } = new();

    /// <summary>
    /// True when nothing was entered. An unanswered question scores zero rather
    /// than being skipped -- skipping it would quietly shrink the denominator
    /// and inflate the result.
    /// </summary>
    public bool IsEmpty =>
        ChoiceIndex is null
        && ChoiceIndices.Count == 0
        && BoolAnswer is null
        && string.IsNullOrWhiteSpace(TextAnswer)
        && BlankAnswers.Values.All(string.IsNullOrWhiteSpace)
        && MatchAnswers.Count == 0
        && SequenceAnswer.Count == 0
        && string.IsNullOrWhiteSpace(EssayAnswer);
}

/// <summary>How one question was marked.</summary>
public sealed class QuestionResult
{
    public required CompiledQuestion Question { get; init; }

    /// <summary>Points awarded. Zero for an essay -- see <see cref="NeedsReview"/>.</summary>
    public required double Scored { get; init; }

    public required double Possible { get; init; }

    /// <summary>
    /// True when this question cannot be marked automatically: an essay.
    ///
    /// Such questions are excluded from the score entirely rather than counted
    /// as zero. Counting them as zero would fail someone who answered
    /// everything that could actually be marked -- a 10-point MC answered
    /// perfectly alongside a 10-point essay would read as 50%, not 100%.
    /// </summary>
    public required bool NeedsReview { get; init; }

    /// <summary>
    /// Whether this counted as correct: at least half its points. Null when it
    /// is not gradeable -- an essay, or a 0-point question.
    /// </summary>
    public bool? IsCorrect => NeedsReview || Possible <= 0
        ? null
        : CompiledQuiz.QuestionIsCorrect(Scored, Possible);

    /// <summary>What was given, for the review report.</summary>
    public required QuestionAnswer Answer { get; init; }

    public bool WasAnswered => !Answer.IsEmpty;
}

/// <summary>The outcome of one sitting.</summary>
public sealed class AttemptResult
{
    public required IReadOnlyList<QuestionResult> Results { get; init; }

    /// <summary>Points scored across the automatically graded questions.</summary>
    public required double ScoredPoints { get; init; }

    /// <summary>
    /// Points available across the automatically graded questions only.
    ///
    /// NOT the paper's total: essays are excluded, so this is the denominator
    /// the percentage is honestly a percentage OF.
    /// </summary>
    public required double AutoGradedPoints { get; init; }

    /// <summary>Points sitting in essays that a person still has to mark.</summary>
    public required double PointsAwaitingReview { get; init; }

    public required int QuestionsAwaitingReview { get; init; }

    /// <summary>
    /// The percentage, or null when nothing could be graded automatically --
    /// an all-essay paper. Null is not zero: showing 0% for a paper nobody has
    /// marked yet would be a lie.
    /// </summary>
    public required double? Percentage { get; init; }

    /// <summary>
    /// Pass, fail, or null when there is nothing to judge.
    /// </summary>
    public required bool? Passed { get; init; }

    public required TimeSpan Elapsed { get; init; }

    /// <summary>True when the timer ran out rather than the taker submitting.</summary>
    public required bool TimedOut { get; init; }

    public required DateTimeOffset TakenAt { get; init; }

    /// <summary>Questions marked wrong, for the review report.</summary>
    public IEnumerable<QuestionResult> Incorrect => Results.Where(r => r.IsCorrect == false);

    /// <summary>Questions a person still has to mark.</summary>
    public IEnumerable<QuestionResult> AwaitingReview => Results.Where(r => r.NeedsReview);

    /// <summary>True when some of the paper could not be marked automatically.</summary>
    public bool HasReviewItems => QuestionsAwaitingReview > 0;
}

/// <summary>
/// Marks a set of answers against a compiled paper.
///
/// Separate from IQuizCompiler on purpose: the compiler decides which questions
/// appear and in what order, and this decides what they were worth. Keeping the
/// pass-mark rules on CompiledQuiz means the Preview tab, the exports and this
/// grader cannot disagree about where the bar is.
/// </summary>
public interface IQuizGrader
{
    AttemptResult Grade(
        CompiledQuiz quiz,
        IReadOnlyDictionary<CompiledQuestion, QuestionAnswer> answers,
        QuizSettings settings,
        TimeSpan elapsed,
        bool timedOut);
}
