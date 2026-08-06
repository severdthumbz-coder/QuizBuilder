using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Builds a spaced-repetition study session for a quiz's flash cards: it works
/// out which cards are due (new ones and ones whose interval has elapsed),
/// orders them, and applies the person's grade to advance the schedule through
/// <see cref="Sm2Scheduler"/> and persist it via <see cref="IReviewProgressStore"/>.
///
/// Cards without stored progress are treated as brand new (due immediately) —
/// so a freshly imported quiz presents every card the first time.
/// </summary>
public sealed class ReviewSession
{
    private readonly IReviewProgressStore _store;
    private readonly Guid _quizId;
    private readonly Dictionary<Guid, StudyCard> _cardsById;

    public ReviewSession(IReviewProgressStore store, QuizDocument document)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        if (document is null) throw new ArgumentNullException(nameof(document));

        _quizId = document.Id;
        _cardsById = document.StudyCards.ToDictionary(c => c.Id);
    }

    /// <summary>The state for every card, seeding new cards with a fresh state.</summary>
    private IEnumerable<ReviewState> AllStates()
    {
        foreach (var card in _cardsById.Values)
        {
            yield return _store.Get(_quizId, card.Id) ?? new ReviewState
            {
                QuizId = _quizId,
                CardId = card.Id,
            };
        }
    }

    /// <summary>
    /// The cards due for review right now, ordered new-first then most-overdue,
    /// paired with their card content. Cards whose interval hasn't elapsed are
    /// omitted.
    /// </summary>
    public IReadOnlyList<StudyCard> DueCards(int today)
    {
        var order = Sm2Scheduler.DueQueue(AllStates(), today);
        var result = new List<StudyCard>(order.Count);
        foreach (var state in order)
        {
            if (_cardsById.TryGetValue(state.CardId, out var card))
                result.Add(card);
        }
        return result;
    }

    /// <summary>Due cards for today.</summary>
    public IReadOnlyList<StudyCard> DueCards() => DueCards(Sm2Scheduler.Today());

    /// <summary>How many cards are due right now.</summary>
    public int DueCount(int today) => DueCards(today).Count;

    /// <summary>
    /// Record a grade for a card: advance its schedule and persist. Returns the
    /// new state. Unknown card ids are ignored (returns null) rather than
    /// throwing, so a stale id from a since-deleted card can't break a session.
    /// </summary>
    public ReviewState? Grade(Guid cardId, ReviewGrade grade, int today)
    {
        if (!_cardsById.ContainsKey(cardId)) return null;

        var current = _store.Get(_quizId, cardId) ?? new ReviewState
        {
            QuizId = _quizId,
            CardId = cardId,
        };

        var next = Sm2Scheduler.Review(current, grade, today);
        _store.Save(next);
        return next;
    }

    /// <summary>Record a grade using today's date.</summary>
    public ReviewState? Grade(Guid cardId, ReviewGrade grade) => Grade(cardId, grade, Sm2Scheduler.Today());
}
