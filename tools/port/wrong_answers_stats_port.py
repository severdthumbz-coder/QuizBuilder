#!/usr/bin/env python3
"""
Python port of two pieces of attempt-history analysis, proven here before any C#:

  1. WRONG-ANSWER SELECTION — "practice only my wrong answers"
  2. PER-QUIZ STATISTICS aggregates

Both read the stored attempt history (AttemptRecord / AttemptQuestionRecord).
The risk that earns a port is entirely in #1's selection rule; #2 is included
because getting an aggregate subtly wrong (counting essays as failures, dividing
by the wrong denominator) is the classic stats bug and cheap to pin here.

------------------------------------------------------------------------------
THE WRONG-ANSWER RULE (decided): "still wrong on your most recent attempt".
------------------------------------------------------------------------------
For a given quiz + taker, look at that taker's attempts. For each QUESTION,
find its MOST RECENT verdict across those attempts and keep the question only if
that verdict is "wrong". So:

  - A question answered wrong last week but RIGHT today is NOT in the set
    (you fixed it — it drops out). This is what makes the set shrink as you
    improve, and it is why "most recent attempt wins" is the whole rule.
  - A question wrong today stays, regardless of older rights.
  - A question that is NOT auto-gradeable on its most recent appearance
    (IsCorrect is None — an essay, or a needs-review answer) is EXCLUDED: we
    cannot assert it was wrong, so we must not drill it as if it were.
  - A question never seen in any attempt is not in the set.

MATCHING records back to the live quiz. New records carry a question_id (the
live Question.Id). Older records (written before this build) have no id, so we
fall back to matching by PROMPT TEXT. Id match is preferred; prompt fallback is
best-effort and only used when id is absent/unknown.

"Most recent" is by the attempt's taken_at, NOT by list order — a caller could
hand us any order, and the newest sitting must win regardless.

The selector returns the set of LIVE question ids to practice (plus, for legacy
rows, resolved via prompt → id using the live quiz). The C# then compiles a
paper from exactly those live questions, so grading/recording is the real flow.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timedelta


# --- Minimal mirrors of the real types (only the fields we reason over) ----- #

@dataclass
class QRecord:
    """Mirror of AttemptQuestionRecord (subset)."""
    prompt: str
    is_correct: bool | None        # None = not auto-gradeable (essay/needs review)
    question_id: str | None = None # None on legacy records


@dataclass
class Attempt:
    """Mirror of AttemptRecord (subset)."""
    taken_at: datetime
    questions: list[QRecord] = field(default_factory=list)
    percentage: float | None = None   # None when nothing auto-gradeable
    passed: bool | None = None
    elapsed_seconds: int = 0


@dataclass
class LiveQuestion:
    """Mirror of a live Question (subset) in the current quiz."""
    id: str
    prompt: str


# --- 1. Wrong-answer selection --------------------------------------------- #

def select_wrong_question_ids(
    live_questions: list[LiveQuestion],
    attempts: list[Attempt],
) -> list[str]:
    """
    Returns the ids of live questions whose MOST RECENT verdict (across the
    given attempts) is wrong. Order follows live_questions, so the practice
    paper reads in quiz order.
    """
    # Resolve each record to a live question id. Prefer the record's own id when
    # it names a live question; else fall back to a prompt match.
    live_by_id = {q.id: q for q in live_questions}
    # First prompt wins if two live questions share a prompt (degenerate), which
    # mirrors the C# dictionary's first-add-wins.
    live_id_by_prompt: dict[str, str] = {}
    for q in live_questions:
        live_id_by_prompt.setdefault(q.prompt, q.id)

    def resolve(rec: QRecord) -> str | None:
        if rec.question_id is not None and rec.question_id in live_by_id:
            return rec.question_id
        return live_id_by_prompt.get(rec.prompt)

    # For each live question id, track the verdict from the most recent attempt
    # that CONTAINED that question. "Most recent" = max taken_at.
    latest_seen: dict[str, datetime] = {}
    latest_verdict: dict[str, bool | None] = {}

    # Process attempts in taken_at order so ties resolve to the later-processed
    # one deterministically; but we also guard with an explicit timestamp check
    # so unsorted input is handled.
    for attempt in sorted(attempts, key=lambda a: a.taken_at):
        for rec in attempt.questions:
            qid = resolve(rec)
            if qid is None:
                continue  # record doesn't map to any live question (deleted)
            prev = latest_seen.get(qid)
            if prev is None or attempt.taken_at >= prev:
                latest_seen[qid] = attempt.taken_at
                # is_correct True/False/None; None means not auto-graded.
                latest_verdict[qid] = rec.is_correct

    wrong_ids = {
        qid for qid, verdict in latest_verdict.items()
        if verdict is False   # strictly wrong; None (not gradeable) is excluded
    }

    # Preserve live quiz order.
    return [q.id for q in live_questions if q.id in wrong_ids]


# --- 2. Per-quiz statistics ------------------------------------------------- #

@dataclass
class QuizStats:
    attempt_count: int
    graded_attempt_count: int           # attempts with a percentage (not all-essay)
    average_percentage: float | None    # over graded attempts only
    best_percentage: float | None
    latest_percentage: float | None
    pass_count: int
    fail_count: int
    average_elapsed_seconds: int | None
    # per-question difficulty: prompt -> (times_wrong, times_graded)
    hardest: list[tuple[str, int, int]] = field(default_factory=list)


def compute_stats(attempts: list[Attempt]) -> QuizStats:
    if not attempts:
        return QuizStats(0, 0, None, None, None, 0, 0, None, [])

    by_time = sorted(attempts, key=lambda a: a.taken_at)

    graded = [a for a in attempts if a.percentage is not None]
    pcts = [a.percentage for a in graded]

    pass_count = sum(1 for a in attempts if a.passed is True)
    fail_count = sum(1 for a in attempts if a.passed is False)

    avg_elapsed = (
        round(sum(a.elapsed_seconds for a in attempts) / len(attempts))
        if attempts else None
    )

    # Per-question difficulty: only count auto-graded appearances (is_correct is
    # not None). times_wrong / times_graded. Essays never inflate "hardest".
    wrong: dict[str, int] = {}
    graded_count: dict[str, int] = {}
    for a in attempts:
        for rec in a.questions:
            if rec.is_correct is None:
                continue
            graded_count[rec.prompt] = graded_count.get(rec.prompt, 0) + 1
            if rec.is_correct is False:
                wrong[rec.prompt] = wrong.get(rec.prompt, 0) + 1

    # Hardest = highest miss RATE, then most misses, then prompt for stability.
    # Only questions graded at least once appear.
    hardest = sorted(
        ((p, wrong.get(p, 0), graded_count[p]) for p in graded_count),
        key=lambda t: (-(t[1] / t[2]), -t[1], t[0]),
    )

    return QuizStats(
        attempt_count=len(attempts),
        graded_attempt_count=len(graded),
        average_percentage=(sum(pcts) / len(pcts)) if pcts else None,
        best_percentage=max(pcts) if pcts else None,
        latest_percentage=by_time[-1].percentage,
        pass_count=pass_count,
        fail_count=fail_count,
        average_elapsed_seconds=avg_elapsed,
        hardest=hardest,
    )


# --- Tests ------------------------------------------------------------------ #

def _check(name, cond):
    print(("PASS " if cond else "FAIL ") + name)
    if not cond:
        raise SystemExit(1)


T0 = datetime(2026, 10, 1, 9, 0, 0)


def at(days):
    return T0 + timedelta(days=days)


def test_recent_right_drops_out():
    live = [LiveQuestion("q1", "P1"), LiveQuestion("q2", "P2")]
    attempts = [
        Attempt(at(0), [QRecord("P1", False, "q1"), QRecord("P2", False, "q2")]),
        # Later: q1 fixed (right), q2 still wrong.
        Attempt(at(1), [QRecord("P1", True, "q1"), QRecord("P2", False, "q2")]),
    ]
    _check("recent-right drops out, recent-wrong stays",
           select_wrong_question_ids(live, attempts) == ["q2"])


def test_recent_wrong_overrides_old_right():
    live = [LiveQuestion("q1", "P1")]
    attempts = [
        Attempt(at(0), [QRecord("P1", True, "q1")]),
        Attempt(at(1), [QRecord("P1", False, "q1")]),  # regressed
    ]
    _check("recent-wrong overrides older right",
           select_wrong_question_ids(live, attempts) == ["q1"])


def test_unsorted_input_still_uses_latest():
    live = [LiveQuestion("q1", "P1")]
    # Newest attempt (right) listed FIRST; older (wrong) second.
    attempts = [
        Attempt(at(5), [QRecord("P1", True, "q1")]),
        Attempt(at(1), [QRecord("P1", False, "q1")]),
    ]
    _check("latest by timestamp, not list order",
           select_wrong_question_ids(live, attempts) == [])


def test_non_gradeable_excluded():
    live = [LiveQuestion("q1", "P1")]
    attempts = [
        Attempt(at(0), [QRecord("P1", False, "q1")]),
        Attempt(at(1), [QRecord("P1", None, "q1")]),  # essay/needs review latest
    ]
    _check("non-gradeable latest verdict excluded",
           select_wrong_question_ids(live, attempts) == [])


def test_legacy_record_matched_by_prompt():
    live = [LiveQuestion("qA", "Capital of France?")]
    attempts = [
        # No question_id (legacy), must match by prompt.
        Attempt(at(0), [QRecord("Capital of France?", False, None)]),
    ]
    _check("legacy record matched by prompt",
           select_wrong_question_ids(live, attempts) == ["qA"])


def test_id_match_preferred_over_stale_prompt():
    # The live question's prompt was edited AFTER the attempt; the record's old
    # prompt no longer matches, but its id does.
    live = [LiveQuestion("q1", "New prompt text")]
    attempts = [
        Attempt(at(0), [QRecord("Old prompt text", False, "q1")]),
    ]
    _check("id match survives a prompt edit",
           select_wrong_question_ids(live, attempts) == ["q1"])


def test_deleted_question_not_in_set():
    live = [LiveQuestion("q1", "P1")]
    attempts = [
        # A record for a question no longer in the quiz (id + prompt both absent
        # from live).
        Attempt(at(0), [QRecord("Deleted?", False, "gone")]),
        Attempt(at(0), [QRecord("P1", False, "q1")]),
    ]
    _check("deleted question excluded, live wrong kept",
           select_wrong_question_ids(live, attempts) == ["q1"])


def test_never_attempted_not_in_set():
    live = [LiveQuestion("q1", "P1"), LiveQuestion("q2", "P2")]
    attempts = [Attempt(at(0), [QRecord("P1", False, "q1")])]
    _check("question never attempted not in set",
           select_wrong_question_ids(live, attempts) == ["q1"])


def test_empty_history_empty_set():
    live = [LiveQuestion("q1", "P1")]
    _check("empty history -> empty set",
           select_wrong_question_ids(live, []) == [])


def test_set_preserves_quiz_order():
    live = [LiveQuestion("q1", "P1"), LiveQuestion("q2", "P2"), LiveQuestion("q3", "P3")]
    attempts = [
        Attempt(at(0), [
            QRecord("P3", False, "q3"),
            QRecord("P1", False, "q1"),
        ]),
    ]
    # Even though the attempt listed q3 before q1, output follows quiz order.
    _check("output preserves live quiz order",
           select_wrong_question_ids(live, attempts) == ["q1", "q3"])


# ----- stats ----- #

def test_stats_basic():
    attempts = [
        Attempt(at(0), [QRecord("P1", True, "q1"), QRecord("P2", False, "q2")],
                percentage=50.0, passed=False, elapsed_seconds=100),
        Attempt(at(1), [QRecord("P1", True, "q1"), QRecord("P2", True, "q2")],
                percentage=100.0, passed=True, elapsed_seconds=200),
    ]
    s = compute_stats(attempts)
    _check("attempt count", s.attempt_count == 2)
    _check("graded count", s.graded_attempt_count == 2)
    _check("average pct", abs(s.average_percentage - 75.0) < 1e-9)
    _check("best pct", s.best_percentage == 100.0)
    _check("latest pct (by time)", s.latest_percentage == 100.0)
    _check("pass/fail counts", s.pass_count == 1 and s.fail_count == 1)
    _check("avg elapsed", s.average_elapsed_seconds == 150)


def test_stats_excludes_essays_from_hardest():
    attempts = [
        Attempt(at(0), [
            QRecord("Gradeable", False, "q1"),
            QRecord("Essay", None, "q2"),
        ], percentage=0.0, passed=False, elapsed_seconds=60),
        Attempt(at(1), [
            QRecord("Gradeable", False, "q1"),
            QRecord("Essay", None, "q2"),
        ], percentage=0.0, passed=False, elapsed_seconds=60),
    ]
    s = compute_stats(attempts)
    prompts = [h[0] for h in s.hardest]
    _check("essay never appears in hardest", "Essay" not in prompts)
    _check("gradeable appears with full miss rate",
           s.hardest[0] == ("Gradeable", 2, 2))


def test_stats_all_essay_attempt_has_no_percentage():
    attempts = [
        Attempt(at(0), [QRecord("Essay", None, "q1")],
                percentage=None, passed=None, elapsed_seconds=30),
    ]
    s = compute_stats(attempts)
    _check("all-essay: no graded attempts", s.graded_attempt_count == 0)
    _check("all-essay: average is None", s.average_percentage is None)
    _check("all-essay: latest percentage is None", s.latest_percentage is None)


def test_stats_hardest_orders_by_miss_rate():
    attempts = [
        Attempt(at(0), [
            QRecord("Easy", True, "q1"),
            QRecord("Medium", False, "q2"),
            QRecord("Hard", False, "q3"),
        ], percentage=33.0, passed=False, elapsed_seconds=90),
        Attempt(at(1), [
            QRecord("Easy", True, "q1"),
            QRecord("Medium", True, "q2"),
            QRecord("Hard", False, "q3"),
        ], percentage=66.0, passed=False, elapsed_seconds=90),
    ]
    s = compute_stats(attempts)
    # Hard missed 2/2 = 1.0, Medium 1/2 = 0.5, Easy 0/2 = 0.0
    _check("hardest ordered by miss rate",
           [h[0] for h in s.hardest] == ["Hard", "Medium", "Easy"])


def test_stats_empty():
    s = compute_stats([])
    _check("empty stats", s.attempt_count == 0 and s.average_percentage is None
           and s.hardest == [])


if __name__ == "__main__":
    test_recent_right_drops_out()
    test_recent_wrong_overrides_old_right()
    test_unsorted_input_still_uses_latest()
    test_non_gradeable_excluded()
    test_legacy_record_matched_by_prompt()
    test_id_match_preferred_over_stale_prompt()
    test_deleted_question_not_in_set()
    test_never_attempted_not_in_set()
    test_empty_history_empty_set()
    test_set_preserves_quiz_order()
    test_stats_basic()
    test_stats_excludes_essays_from_hardest()
    test_stats_all_essay_attempt_has_no_percentage()
    test_stats_hardest_orders_by_miss_rate()
    test_stats_empty()
    print("\nAll wrong-answers / stats port checks passed.")
