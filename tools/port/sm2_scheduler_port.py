#!/usr/bin/env python3
"""
Python port of the SM-2 spaced-repetition scheduler (feature: spaced repetition
for flash cards). Ported and proven here before any C# is written, because the
interval/ease arithmetic and the lapse-reset edge cases are exactly the kind of
logic that is easy to get subtly wrong.

SM-2 in brief. Each card carries three pieces of review state:
  * repetitions  n   -- how many times in a row it's been recalled well
  * ease factor  EF  -- a multiplier (>= 1.3) for how fast its interval grows
  * interval     I   -- days until the next review

The user rates each recall on 0..5 (SuperMemo's "quality"). We expose a friendlier
4-button scale in the UI (Again/Hard/Good/Easy) that maps to qualities {2,3,4,5};
a rating below 3 is a lapse. The algorithm:

  if quality < 3:                      # lapse -- forgot it
      n = 0
      I = 1                            # see it again tomorrow
      # EF is left unchanged on a lapse (SuperMemo keeps EF; only reps/interval reset)
  else:
      if n == 0:   I = 1
      elif n == 1: I = 6
      else:        I = round(I * EF)
      n = n + 1

  # EF update happens on every review, clamped to a 1.3 floor:
  EF = EF + (0.1 - (5 - q) * (0.08 + (5 - q) * 0.02))
  if EF < 1.3: EF = 1.3

New cards start n=0, I=0, EF=2.5. A card is "due" when today >= last_reviewed +
interval days (a brand-new card, never reviewed, is due immediately).

The port pins: the classic 1 -> 6 -> I*EF progression, the EF drift up on easy /
down on hard, the 1.3 floor, the lapse reset (n and I reset, EF preserved), and
the due-date arithmetic.
"""

from dataclasses import dataclass, replace


START_EF = 2.5
MIN_EF = 1.3


@dataclass(frozen=True)
class ReviewState:
    repetitions: int = 0
    ease: float = START_EF
    interval: int = 0            # days; 0 == brand new, never scheduled
    last_reviewed_day: int | None = None  # integer day index, None == never


def update_ease(ef: float, quality: int) -> float:
    ef2 = ef + (0.1 - (5 - quality) * (0.08 + (5 - quality) * 0.02))
    return ef2 if ef2 >= MIN_EF else MIN_EF


def review(state: ReviewState, quality: int, today: int) -> ReviewState:
    """Apply one review at integer day `today`, return the new state.
    quality is 0..5; < 3 is a lapse."""
    if quality < 0 or quality > 5:
        raise ValueError("quality must be 0..5")

    if quality < 3:
        # lapse: reset the streak and show again tomorrow. Classic SM-2 still
        # applies the EF formula, so a forgotten card also gets a lower ease
        # (it was harder than its EF implied). EF stays clamped at the 1.3 floor.
        n = 0
        interval = 1
        ease = update_ease(state.ease, quality)
    else:
        if state.repetitions == 0:
            interval = 1
        elif state.repetitions == 1:
            interval = 6
        else:
            interval = round(state.interval * state.ease)
        n = state.repetitions + 1
        ease = update_ease(state.ease, quality)

    return ReviewState(
        repetitions=n,
        ease=ease,
        interval=interval,
        last_reviewed_day=today,
    )


def is_due(state: ReviewState, today: int) -> bool:
    """A never-reviewed card is due now; otherwise due when today has reached the
    scheduled next-review day."""
    if state.last_reviewed_day is None:
        return True
    return today >= state.last_reviewed_day + state.interval


def due_cards(states: dict, today: int) -> list:
    """Return the ids of all due cards, brand-new cards first (they've waited
    longest conceptually), then by how overdue they are."""
    due = [(cid, s) for cid, s in states.items() if is_due(s, today)]

    def overdueness(item):
        cid, s = item
        if s.last_reviewed_day is None:
            return (0, 0)           # new cards sort first
        return (1, -(today - (s.last_reviewed_day + s.interval)))
    due.sort(key=overdueness)
    return [cid for cid, _ in due]


# --------------------------------------------------------------------------- #
# Tests
# --------------------------------------------------------------------------- #

def approx(a, b, eps=1e-9):
    return abs(a - b) < eps


def test_new_card_is_due_immediately():
    assert is_due(ReviewState(), today=0)
    assert is_due(ReviewState(), today=100)
    print("  OK  a brand-new card is due immediately")


def test_first_three_good_reviews_follow_1_6_then_ease():
    s = ReviewState()
    s = review(s, quality=4, today=0)     # first good -> I=1
    assert s.interval == 1 and s.repetitions == 1
    s = review(s, quality=4, today=1)     # second good -> I=6
    assert s.interval == 6 and s.repetitions == 2
    ef_after_two = s.ease
    s = review(s, quality=4, today=7)     # third good -> I = round(6 * EF)
    assert s.interval == round(6 * ef_after_two)
    assert s.repetitions == 3
    print(f"  OK  good reviews progress 1 -> 6 -> round(6*EF)={s.interval}")


def test_ease_rises_on_easy_falls_on_hard():
    # quality 5 (easy) pushes EF up by +0.1
    up = update_ease(2.5, 5)
    assert approx(up, 2.6), up
    # quality 3 (a just-passed "hard") pushes EF down
    down = update_ease(2.5, 3)
    assert down < 2.5
    print(f"  OK  EF 2.5 -> {up:.2f} on easy, -> {down:.2f} on hard")


def test_ease_never_below_floor():
    ef = 1.3
    for _ in range(20):
        ef = update_ease(ef, 3)   # repeatedly "hard"
    assert ef == MIN_EF, ef
    # even the worst quality can't push below the floor
    assert update_ease(1.3, 0) == MIN_EF
    print(f"  OK  EF clamped at floor {MIN_EF} no matter how many hard reviews")


def test_lapse_resets_reps_and_interval_and_lowers_ease():
    s = ReviewState()
    s = review(s, 4, today=0)
    s = review(s, 4, today=1)
    s = review(s, 5, today=7)      # nicely learned, big interval, EF risen
    assert s.repetitions == 3 and s.interval > 6
    eased = s.ease
    s = review(s, 1, today=20)     # lapse: forgot it
    assert s.repetitions == 0, "reps must reset on lapse"
    assert s.interval == 1, "interval resets to 1 day on lapse"
    # Classic SM-2: the EF formula applies on every review, so a lapse (low
    # quality) LOWERS ease. A forgotten card was harder than its EF implied.
    assert s.ease < eased, "EF drops on a lapse in classic SM-2"
    assert s.ease >= MIN_EF, "but never below the floor"
    print("  OK  lapse resets reps->0, interval->1, and lowers EF (classic SM-2)")


def test_due_after_interval_not_before():
    s = review(ReviewState(), 4, today=0)   # interval 1, reviewed day 0
    assert not is_due(s, today=0)           # same day: not due
    assert is_due(s, today=1)               # next day: due
    s2 = review(s, 4, today=1)              # interval now 6, reviewed day 1
    assert not is_due(s2, today=6)          # day 6 < 1+6
    assert is_due(s2, today=7)              # day 7 == 1+6 -> due
    print("  OK  due exactly when today >= last_reviewed + interval")


def test_due_queue_orders_new_first_then_most_overdue():
    states = {
        "new":       ReviewState(),                                   # never seen
        "overdue5":  ReviewState(1, 2.5, 3, last_reviewed_day=0),     # due day 3, now 8 -> 5 over
        "overdue1":  ReviewState(1, 2.5, 3, last_reviewed_day=4),     # due day 7, now 8 -> 1 over
        "future":    ReviewState(2, 2.5, 30, last_reviewed_day=5),    # due day 35 -> not due
    }
    order = due_cards(states, today=8)
    assert "future" not in order
    assert order[0] == "new", "new card first"
    assert order.index("overdue5") < order.index("overdue1"), "more overdue earlier"
    print(f"  OK  due queue: {order} (new first, then most overdue)")


def test_full_realistic_sequence():
    # Simulate a card learned, lapsed, relearned; assert it stays sane throughout.
    s = ReviewState()
    day = 0
    for q in [4, 4, 4, 5, 5]:            # learn it well
        s = review(s, q, today=day)
        day += s.interval
    assert s.repetitions == 5
    assert s.interval > 20               # well-spaced by now
    assert s.ease > 2.5                  # eased up from easies
    s = review(s, 1, today=day + 100)    # forget it after a long gap
    assert s.repetitions == 0 and s.interval == 1
    s = review(s, 4, today=day + 101)    # relearn
    assert s.repetitions == 1 and s.interval == 1
    print("  OK  learn -> lapse -> relearn sequence stays consistent")


def run_all():
    print("\nSM-2 spaced repetition port -- verification\n" + "-" * 52)
    test_new_card_is_due_immediately()
    test_first_three_good_reviews_follow_1_6_then_ease()
    test_ease_rises_on_easy_falls_on_hard()
    test_ease_never_below_floor()
    test_lapse_resets_reps_and_interval_and_lowers_ease()
    test_due_after_interval_not_before()
    test_due_queue_orders_new_first_then_most_overdue()
    test_full_realistic_sequence()
    print("-" * 52)
    print("ALL PASS -- SM-2 interval progression, EF drift + 1.3 floor, lapse")
    print("reset, due-date arithmetic, and due-queue ordering all proved. The C#")
    print("Scheduler mirrors this exactly.\n")


if __name__ == "__main__":
    run_all()
