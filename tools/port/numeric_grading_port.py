#!/usr/bin/env python3
"""
Python port of NumericQuestion grading (new question type, feature: numeric).

A numeric question has a target value and an optional tolerance. The taker types
a number (stored as text, like short-answer); grading parses it and checks
whether it falls within [target - tolerance, target + tolerance]. Tolerance
defaults to 0, meaning an exact match.

The risk here is entirely in parsing and comparison:
  * blank / whitespace         -> wrong (0), never a crash
  * non-numeric ("abc", "3x")  -> wrong (0)
  * integer vs decimal ("3" vs "3.0") -> equal
  * negative numbers, leading +        -> parsed
  * scientific notation ("1e3")        -> parsed (accepted; some takers use it)
  * surrounding whitespace ("  3.14 ") -> trimmed then parsed
  * exact match with tolerance 0        -> only the exact value is correct
  * boundary values (target +/- tolerance exactly) -> INCLUSIVE (<=)
  * a tiny float epsilon past the boundary -> wrong
  * negative tolerance (author error)   -> treated as 0 (exact), never widens

Parsing deliberately uses invariant/period-decimal parsing (not locale comma),
matching how the .qbx stores numbers and how the C# grader will parse with
CultureInfo.InvariantCulture. Comma-decimal input is out of scope by design;
documented so a future locale story is a conscious addition.
"""

from dataclasses import dataclass
from typing import Optional


@dataclass
class NumericQuestion:
    target: float
    tolerance: float = 0.0
    points: float = 1.0


def try_parse_number(text: Optional[str]) -> Optional[float]:
    """Invariant-culture float parse. Returns None for blank/garbage rather than
    raising — the grader treats None as a wrong answer."""
    if text is None:
        return None
    s = text.strip()
    if not s:
        return None
    try:
        # Python's float() accepts leading +, scientific notation, and a period
        # decimal — the same shapes C#'s double.TryParse(InvariantCulture) accepts.
        # Reject things float() would otherwise allow that we don't want:
        # "inf"/"nan" are not valid quiz answers.
        low = s.lower()
        if "inf" in low or "nan" in low:
            return None
        return float(s)
    except ValueError:
        return None


def score_numeric(question: NumericQuestion, text_answer: Optional[str]) -> float:
    """Full points if the parsed answer is within tolerance of target, else 0."""
    value = try_parse_number(text_answer)
    if value is None:
        return 0.0

    # An author-entered negative tolerance must never widen the window; clamp to 0.
    tol = question.tolerance if question.tolerance > 0 else 0.0

    if abs(value - question.target) <= tol:
        return question.points
    return 0.0


def is_empty(text_answer: Optional[str]) -> bool:
    """Mirror QuestionAnswer.IsEmpty for the numeric field (reuses TextAnswer)."""
    return text_answer is None or not text_answer.strip()


# --------------------------------------------------------------------------- #
# Tests
# --------------------------------------------------------------------------- #

def test_exact_match_no_tolerance():
    q = NumericQuestion(target=3.14, tolerance=0.0)
    assert score_numeric(q, "3.14") == 1.0
    assert score_numeric(q, "3.15") == 0.0
    print("  OK  exact match (tolerance 0): only the exact value scores")


def test_integer_decimal_equivalence():
    q = NumericQuestion(target=3.0)
    assert score_numeric(q, "3") == 1.0
    assert score_numeric(q, "3.0") == 1.0
    assert score_numeric(q, "3.00") == 1.0
    print("  OK  '3' == '3.0' == '3.00'")


def test_within_tolerance():
    q = NumericQuestion(target=10.0, tolerance=0.5)
    assert score_numeric(q, "10.4") == 1.0
    assert score_numeric(q, "9.6") == 1.0
    assert score_numeric(q, "10.6") == 0.0
    assert score_numeric(q, "9.4") == 0.0
    print("  OK  within +/- tolerance scores, outside doesn't")


def test_boundary_is_inclusive():
    q = NumericQuestion(target=10.0, tolerance=0.5)
    assert score_numeric(q, "10.5") == 1.0  # exactly at +tol
    assert score_numeric(q, "9.5") == 1.0   # exactly at -tol
    print("  OK  boundary target +/- tolerance is inclusive")


def test_blank_and_garbage_are_wrong():
    q = NumericQuestion(target=5.0, tolerance=1.0)
    for bad in [None, "", "   ", "abc", "3x", "five", "3.1.4", "--3"]:
        assert score_numeric(q, bad) == 0.0, f"{bad!r} should score 0"
    print("  OK  blank / non-numeric -> 0, no crash")


def test_negative_numbers():
    q = NumericQuestion(target=-5.0, tolerance=0.1)
    assert score_numeric(q, "-5") == 1.0
    assert score_numeric(q, "-5.05") == 1.0
    assert score_numeric(q, "5") == 0.0
    print("  OK  negative targets and answers")


def test_leading_plus_and_whitespace():
    q = NumericQuestion(target=42.0)
    assert score_numeric(q, "+42") == 1.0
    assert score_numeric(q, "  42  ") == 1.0
    print("  OK  leading '+' and surrounding whitespace tolerated")


def test_scientific_notation():
    q = NumericQuestion(target=1000.0, tolerance=0.0)
    assert score_numeric(q, "1e3") == 1.0
    assert score_numeric(q, "1E3") == 1.0
    print("  OK  scientific notation accepted")


def test_inf_nan_rejected():
    q = NumericQuestion(target=0.0, tolerance=1e308)
    assert score_numeric(q, "inf") == 0.0
    assert score_numeric(q, "nan") == 0.0
    assert score_numeric(q, "Infinity") == 0.0
    print("  OK  inf / nan rejected as answers")


def test_negative_tolerance_clamped():
    # an author error: negative tolerance must not widen or invert the window
    q = NumericQuestion(target=10.0, tolerance=-5.0)
    assert score_numeric(q, "10") == 1.0    # exact still correct
    assert score_numeric(q, "12") == 0.0    # not widened
    print("  OK  negative tolerance clamped to exact match")


def test_points_respected():
    q = NumericQuestion(target=7.0, tolerance=0.0, points=2.5)
    assert score_numeric(q, "7") == 2.5
    assert score_numeric(q, "8") == 0.0
    print("  OK  question points respected")


def test_tiny_epsilon_past_boundary_is_wrong():
    q = NumericQuestion(target=1.0, tolerance=0.1)
    assert score_numeric(q, "1.1000000001") == 0.0
    print("  OK  a hair past the boundary is wrong")


def run_all():
    print("\nNumeric grading port -- verification\n" + "-" * 52)
    test_exact_match_no_tolerance()
    test_integer_decimal_equivalence()
    test_within_tolerance()
    test_boundary_is_inclusive()
    test_blank_and_garbage_are_wrong()
    test_negative_numbers()
    test_leading_plus_and_whitespace()
    test_scientific_notation()
    test_inf_nan_rejected()
    test_negative_tolerance_clamped()
    test_points_respected()
    test_tiny_epsilon_past_boundary_is_wrong()
    print("-" * 52)
    print("ALL PASS -- numeric parse + tolerance comparison proved, including")
    print("blank/garbage/inf/nan/negative-tolerance edge cases. Safe to port.\n")


if __name__ == "__main__":
    run_all()
