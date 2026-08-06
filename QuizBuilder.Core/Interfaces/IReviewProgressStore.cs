using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// Stores per-user spaced-repetition progress. This is personal state — how well
/// <em>this</em> person knows each card — so it lives beside the app, never in
/// the shareable .qbx. State is keyed by (quiz id, card id) so it survives the
/// quiz being re-imported and never bloats the distributed file.
/// </summary>
public interface IReviewProgressStore
{
    /// <summary>The stored state for a card, or null if it has never been reviewed.</summary>
    ReviewState? Get(Guid quizId, Guid cardId);

    /// <summary>All stored states for a quiz (only cards reviewed at least once).</summary>
    IReadOnlyList<ReviewState> ForQuiz(Guid quizId);

    /// <summary>Persist the state for a card, replacing any prior state.</summary>
    void Save(ReviewState state);

    /// <summary>Forget all progress for a quiz (e.g. "reset my progress").</summary>
    void ClearQuiz(Guid quizId);

    /// <summary>Raised whenever stored progress changes.</summary>
    event EventHandler? ProgressChanged;
}
