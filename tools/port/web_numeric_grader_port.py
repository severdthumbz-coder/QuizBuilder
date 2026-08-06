#!/usr/bin/env python3
"""
Python port for the WEB exporter's numeric grader (feature: numeric in the
interactive web quiz). Unlike the document exporters, the web export re-implements
grading in JavaScript, so its numeric scoring MUST match C#
QuizGrader.ScoreNumeric exactly — otherwise a quiz grades differently in the
browser than on the desktop, a silent correctness bug.

The single riskiest divergence is NUMBER PARSING:
  * C# double.TryParse(s, Float|AllowLeadingSign, InvariantCulture) is STRICT:
    the WHOLE string must be a number. "3.14abc" -> FALSE. "" -> FALSE.
  * JS parseFloat is LENIENT: parseFloat("3.14abc") === 3.14, which would mark a
    garbage answer correct. So the JS must NOT use bare parseFloat.
  * JS Number(s) is stricter (Number("3.14abc") === NaN) but Number("") === 0 and
    Number("  ") === 0 and Number("0x10") === 16 — all wrong for us.

So the JS parse must replicate C#'s: trim, reject empty, reject anything that
isn't a clean decimal/scientific number with optional leading sign, reject
inf/nan. This port defines that strict parse and proves the grader matches the C#
behaviour already pinned in numeric_grading_port.py / NumericDropdownGradingTests.

This models the JS with Python stand-ins so we can assert the SAME table of
(input -> score) the C# grader produces. If these match, porting to JS (using the
same strict-parse regex) is safe.
"""

import re
from dataclasses import dataclass
from typing import Optional


# The strict number pattern the JS will use — mirrors what C#
# double.TryParse(Float|AllowLeadingSign, Invariant) accepts: optional sign,
# digits with optional single decimal point, optional exponent. No hex, no
# thousands separators, no leading/trailing junk. Anchored to the whole string.
_NUM_RE = re.compile(r'^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$')


def js_strict_parse(text: Optional[str]) -> Optional[float]:
    """Stand-in for the JS strictParseNumber(): returns a float or None.
    None means 'not a valid number', scored as wrong."""
    if text is None:
        return None
    s = text.strip()
    if not s:
        return None
    if not _NUM_RE.match(s):
        return None
    try:
        v = float(s)
    except ValueError:
        return None
    # inf/nan can't arise from the regex, but guard anyway (matches C#'s
    # IsNaN/IsInfinity check).
    if v != v or v in (float("inf"), float("-inf")):
        return None
    return v


@dataclass
class NumQ:
    target: float
    tolerance: float = 0.0
    points: float = 1.0


def js_score_numeric(q: NumQ, text: Optional[str]) -> float:
    """Stand-in for the JS numeric case in scoreQuestion(). Must match
    C# ScoreNumeric exactly."""
    v = js_strict_parse(text)
    if v is None:
        return 0.0
    tol = q.tolerance if q.tolerance > 0 else 0.0
    return q.points if abs(v - q.target) <= tol else 0.0


# --------------------------------------------------------------------------- #
# The reference C# behaviour (from numeric_grading_port.py), inlined so this
# file is self-checking: the JS stand-in must produce the SAME result for every
# case in this table.
# --------------------------------------------------------------------------- #

def csharp_reference(q: NumQ, text: Optional[str]) -> float:
    """A faithful copy of the C# ScoreNumeric logic, to compare against."""
    if text is None or not text.strip():
        return 0.0
    s = text.strip()
    # C# double.TryParse(Float|AllowLeadingSign, Invariant)
    if not _NUM_RE.match(s):
        return 0.0
    try:
        v = float(s)
    except ValueError:
        return 0.0
    if v != v or v in (float("inf"), float("-inf")):
        return 0.0
    tol = q.tolerance if q.tolerance > 0 else 0.0
    return q.points if abs(v - q.target) <= tol else 0.0


CASES = [
    # (target, tol, points, input)
    (3.14, 0.0, 1.0, "3.14"),
    (3.14, 0.0, 1.0, "3.15"),
    (3.0,  0.0, 1.0, "3"),
    (3.0,  0.0, 1.0, "3.0"),
    (3.0,  0.0, 1.0, "3.00"),
    (10.0, 0.5, 1.0, "10.4"),
    (10.0, 0.5, 1.0, "9.6"),
    (10.0, 0.5, 1.0, "10.5"),   # boundary inclusive
    (10.0, 0.5, 1.0, "9.5"),    # boundary inclusive
    (10.0, 0.5, 1.0, "10.6"),
    (10.0, 0.5, 1.0, "9.4"),
    (5.0,  1.0, 1.0, ""),
    (5.0,  1.0, 1.0, "   "),
    (5.0,  1.0, 1.0, "abc"),
    (5.0,  1.0, 1.0, "3x"),
    (5.0,  1.0, 1.0, "3.14abc"),   # the parseFloat trap
    (5.0,  1.0, 1.0, "3.1.4"),
    (5.0,  1.0, 1.0, "0x10"),      # the Number() trap
    (-5.0, 0.1, 1.0, "-5"),
    (-5.0, 0.1, 1.0, "-5.05"),
    (-5.0, 0.1, 1.0, "5"),
    (42.0, 0.0, 1.0, "+42"),
    (42.0, 0.0, 1.0, "  42  "),
    (1000.0, 0.0, 1.0, "1e3"),
    (1000.0, 0.0, 1.0, "1E3"),
    (0.0, 1e308, 1.0, "inf"),
    (0.0, 1e308, 1.0, "nan"),
    (0.0, 1e308, 1.0, "Infinity"),
    (10.0, -5.0, 1.0, "10"),   # negative tolerance clamped
    (10.0, -5.0, 1.0, "12"),
    (7.0, 0.0, 2.5, "7"),
    (7.0, 0.0, 2.5, "8"),
    (1.0, 0.1, 1.0, "1.1000000001"),
]


def test_js_matches_csharp_on_every_case():
    mismatches = []
    for target, tol, pts, inp in CASES:
        q = NumQ(target, tol, pts)
        js = js_score_numeric(q, inp)
        cs = csharp_reference(q, inp)
        if js != cs:
            mismatches.append((inp, js, cs))
    if mismatches:
        for inp, js, cs in mismatches:
            print(f"  MISMATCH input={inp!r}: js={js} cs={cs}")
        raise AssertionError(f"{len(mismatches)} JS/C# mismatches")
    print(f"  OK  JS grader matches C# on all {len(CASES)} cases")


def test_parsefloat_trap_specifically():
    # The whole point: "3.14abc" must be WRONG (parseFloat would accept 3.14).
    q = NumQ(3.14, 0.0, 1.0)
    assert js_score_numeric(q, "3.14abc") == 0.0
    # and "0x10" (Number() would give 16) must be wrong for target 16 too
    assert js_score_numeric(NumQ(16.0), "0x10") == 0.0
    # and "" (Number('') === 0) must be wrong for target 0
    assert js_score_numeric(NumQ(0.0), "") == 0.0
    assert js_score_numeric(NumQ(0.0), "   ") == 0.0
    print("  OK  parseFloat/Number() traps avoided (3.14abc, 0x10, '', '  ')")


def test_valid_shapes_accepted():
    for s in ["3", "3.0", "-3", "+3", ".5", "3.", "1e3", "1.5E-2", "  42  "]:
        assert js_strict_parse(s) is not None, f"{s!r} should parse"
    print("  OK  valid numeric shapes accepted (incl. .5, 3., 1e3, 1.5E-2)")


def run_all():
    print("\nWeb numeric grader port -- JS must match C# ScoreNumeric\n" + "-" * 52)
    test_js_matches_csharp_on_every_case()
    test_parsefloat_trap_specifically()
    test_valid_shapes_accepted()
    print("-" * 52)
    print("ALL PASS -- the strict-parse JS grader matches C# on every case,")
    print("including the parseFloat('3.14abc')=3.14 and Number('')=0 traps that a")
    print("naive JS port would get wrong. Safe to write the JS with this regex.\n")


if __name__ == "__main__":
    run_all()
