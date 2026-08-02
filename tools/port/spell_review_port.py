#!/usr/bin/env python3
"""
Python port of the spell-review pipeline (feature B, engine layer).

The Hunspell call itself is NOT modelled here -- that is an App-only dependency
whose real behaviour only the Windows build can confirm. What IS modelled and
proved here is everything AROUND the dictionary lookup, which is pure logic and
therefore belongs in Core where it can be unit-tested:

  1. TOKENIZATION  -- split a field's text into checkable word tokens, each with
     its (start, length) span so the UI can highlight and a Replace can splice.
  2. EXCLUSIONS    -- tokens that must never be flagged regardless of dictionary:
       * fill-in-the-blank placeholders  {{1}}, {{2}}, ...
       * pure numbers / numeric-with-unit (2, 3.5, 100%, 12px)
       * URLs and emails
       * ALL-CAPS acronyms (NASA, HTTP) -- almost always intentional
       * single characters
  3. IGNORE-LIST   -- the user's custom dictionary. Matching is case-insensitive
     and whitespace-trimmed, mirroring the taker-email normalization already in
     Core (TakerKey), so "Photosynthesis", "photosynthesis " and "photosynthesis"
     are the same ignored word.
  4. DE-DUP        -- the same misspelling repeated across many fields collapses
     to one issue carrying all its occurrences, so the panel shows "somme (4)"
     not four identical rows.

The dictionary is injected as a predicate `is_known(word) -> bool`. In Core the
real provider passes Hunspell; here the tests pass a fake set. This is the seam
that keeps the pipeline testable without the engine.
"""

from dataclasses import dataclass, field
from typing import Callable, List, Set, Tuple
import re


# --------------------------------------------------------------------------- #
# Tokenization
# --------------------------------------------------------------------------- #

# A "word" for spell-check: letters, with internal apostrophes/hyphens allowed
# (don't -> one token; mother-in-law -> one token). Deliberately Unicode-aware
# via \w minus digits handling below.
_WORD_RE = re.compile(r"[^\W\d_](?:[\w'\-]*[^\W\d_])?", re.UNICODE)

_BLANK_TOKEN_RE = re.compile(r"\{\{\d+\}\}")
_URL_RE = re.compile(r"(https?://|www\.)\S+", re.IGNORECASE)
_EMAIL_RE = re.compile(r"\S+@\S+\.\S+")


@dataclass(frozen=True)
class Token:
    text: str
    start: int
    length: int


def tokenize(text: str) -> List[Token]:
    """Yield word tokens with spans. Blank tokens / URLs / emails are masked out
    first so their internal letters are never surfaced as words."""
    if not text:
        return []

    # Mask spans we must not tokenize into words, preserving length/offsets by
    # replacing each masked char with a space (keeps every later index correct).
    masked = list(text)
    for rx in (_BLANK_TOKEN_RE, _URL_RE, _EMAIL_RE):
        for m in rx.finditer(text):
            for i in range(m.start(), m.end()):
                masked[i] = " "
    masked_text = "".join(masked)

    toks = []
    for m in _WORD_RE.finditer(masked_text):
        # A word token that directly abuts a digit in the ORIGINAL text is part
        # of an alphanumeric run (mp3 -> "mp" touches '3'; 3d -> "d" touches
        # '3'). Such fragments are not real words; skip them at the source so
        # "mp" from "mp3" never becomes a token. Checked here, not in
        # is_excluded_token, because the fragment itself is digit-free.
        before = text[m.start() - 1] if m.start() > 0 else ""
        after = text[m.end()] if m.end() < len(text) else ""
        if before.isdigit() or after.isdigit():
            continue
        toks.append(Token(m.group(), m.start(), len(m.group())))
    return toks


# --------------------------------------------------------------------------- #
# Exclusions (never flagged regardless of dictionary)
# --------------------------------------------------------------------------- #

def is_excluded_token(tok: Token) -> bool:
    w = tok.text
    if len(w) <= 1:
        return True                       # single letters: "a", "I", stray "x"
    if w.isupper() and len(w) <= 5:
        return True                       # short ALL-CAPS acronym (NASA, HTTP)
    if any(ch.isdigit() for ch in w):
        return True                       # alphanumerics like h2o, mp3 -- noisy
    return False


# --------------------------------------------------------------------------- #
# Ignore-list normalization (mirrors Core TakerKey: trim + casefold)
# --------------------------------------------------------------------------- #

def normalize_ignore(word: str) -> str:
    return word.strip().casefold()


def build_ignore_set(words: List[str]) -> Set[str]:
    return {normalize_ignore(w) for w in words if w and w.strip()}


# --------------------------------------------------------------------------- #
# Review pipeline
# --------------------------------------------------------------------------- #

@dataclass
class Occurrence:
    field_id: str          # opaque id of the TextField (section+question+kind in C#)
    start: int
    length: int


@dataclass
class Issue:
    word: str                              # the misspelled surface form (first seen)
    suggestions: List[str]
    occurrences: List[Occurrence] = field(default_factory=list)

    @property
    def count(self) -> int:
        return len(self.occurrences)


def review_fields(
    fields: List[Tuple[str, str]],         # (field_id, text)
    is_known: Callable[[str], bool],       # dictionary predicate (Hunspell in prod)
    suggest: Callable[[str], List[str]],   # suggestion source (Hunspell in prod)
    ignore_words: List[str],
) -> List[Issue]:
    """Run the whole pipeline. Returns de-duped issues in first-seen order,
    each carrying every occurrence across all fields."""
    ignore = build_ignore_set(ignore_words)
    by_key: dict[str, Issue] = {}
    order: List[str] = []

    for field_id, text in fields:
        for tok in tokenize(text):
            if is_excluded_token(tok):
                continue
            key = tok.text.casefold()
            if key in ignore:
                continue
            if is_known(tok.text):
                continue
            # A real misspelling.
            if key not in by_key:
                by_key[key] = Issue(word=tok.text, suggestions=suggest(tok.text))
                order.append(key)
            by_key[key].occurrences.append(
                Occurrence(field_id=field_id, start=tok.start, length=tok.length))

    return [by_key[k] for k in order]


# --------------------------------------------------------------------------- #
# Tests
# --------------------------------------------------------------------------- #

# A tiny fake dictionary standing in for Hunspell.
_KNOWN = {
    "the", "quick", "brown", "fox", "a", "description", "with", "some",
    "misspellings", "photosynthesis", "cell", "mitochondria", "answer",
    "colour", "color", "step", "one", "two", "three", "match", "left", "right",
    "front", "back", "term", "definition", "prompt", "hint", "quiz", "title",
    "section", "and", "is", "are", "of", "in",
}

def _fake_is_known(w: str) -> bool:
    return w.casefold() in _KNOWN

def _fake_suggest(w: str) -> List[str]:
    # crude: offer the known word with smallest edit-ish overlap; enough to test
    # that suggestions flow through, not to test suggestion quality.
    cands = sorted(_KNOWN, key=lambda k: (abs(len(k) - len(w)), k))
    return [c for c in cands if c and c[0] == w.casefold()[0]][:3]


def test_tokenize_spans_are_correct():
    toks = tokenize("the quick fox")
    assert [(t.text, t.start, t.length) for t in toks] == [
        ("the", 0, 3), ("quick", 4, 5), ("fox", 10, 3)]
    print("  OK  tokenize: spans map back to the exact source offsets")


def test_blank_tokens_not_flagged():
    # {{1}} must not become word tokens "1" or leak braces
    text = "fill in {{1}} and {{2}} please"
    words = [t.text for t in tokenize(text)]
    assert words == ["fill", "in", "and", "please"], words
    print("  OK  exclusions: {{n}} blank placeholders never tokenized")


def test_urls_and_emails_masked():
    text = "see https://example.com/x or mail a@b.com now"
    words = [t.text for t in tokenize(text)]
    assert "https" not in words and "example" not in words
    assert "com" not in words and "b" not in words
    assert words == ["see", "or", "mail", "now"], words
    print("  OK  exclusions: URLs and emails masked out whole")


def test_numeric_and_acronym_excluded():
    toks = tokenize("NASA sent 3 rockets and h2o mp3 files")
    kept = [t.text for t in toks if not is_excluded_token(t)]
    # NASA excluded (short caps), 3 not a word token, h2o/mp3 excluded (digits)
    assert kept == ["sent", "rockets", "and", "files"], kept
    print("  OK  exclusions: short ALL-CAPS acronyms and alphanumerics skipped")


def test_ignore_list_case_and_space_insensitive():
    fields = [("f1", "Photosynthesis in the Mitochondria")]
    # Pretend the dictionary does NOT know these two (remove from known set view)
    def unknown_bio(w): return w.casefold() in (_KNOWN - {"photosynthesis", "mitochondria"})
    # With no ignore-list, both are flagged:
    issues = review_fields(fields, unknown_bio, _fake_suggest, ignore_words=[])
    flagged = sorted(i.word.casefold() for i in issues)
    assert flagged == ["mitochondria", "photosynthesis"], flagged
    # Ignoring with different case + trailing space suppresses both:
    issues2 = review_fields(fields, unknown_bio, _fake_suggest,
                            ignore_words=["  PHOTOSYNTHESIS ", "Mitochondria"])
    assert issues2 == [], [i.word for i in issues2]
    print("  OK  ignore-list: case-insensitive + whitespace-trimmed (mirrors TakerKey)")


def test_dedup_collapses_repeats_with_all_occurrences():
    fields = [
        ("f1", "somme text with somme"),      # 2 occurrences here
        ("f2", "and somme more"),             # 1 occurrence here
    ]
    def unknown(w): return w.casefold() != "somme"
    issues = review_fields(fields, unknown, _fake_suggest, ignore_words=[])
    assert len(issues) == 1, [i.word for i in issues]
    iss = issues[0]
    assert iss.word == "somme"
    assert iss.count == 3, iss.count
    # occurrences carry the right fields and offsets
    assert [(o.field_id, o.start) for o in iss.occurrences] == [
        ("f1", 0), ("f1", 16), ("f2", 4)], [(o.field_id, o.start) for o in iss.occurrences]
    print("  OK  de-dup: repeated misspelling -> one issue with every occurrence")


def test_known_words_never_flagged():
    fields = [("f1", "the quick brown fox")]
    issues = review_fields(fields, _fake_is_known, _fake_suggest, ignore_words=[])
    assert issues == [], [i.word for i in issues]
    print("  OK  clean text: all-known words produce zero issues")


def test_suggestions_flow_through():
    fields = [("f1", "colour and color")]   # 'color' unknown, 'colour' known
    def unknown(w): return w.casefold() in _KNOWN and w.casefold() != "color"
    issues = review_fields(fields, unknown, _fake_suggest, ignore_words=[])
    assert len(issues) == 1 and issues[0].word == "color"
    assert issues[0].suggestions, "expected at least one suggestion"
    assert all(s[0] == "c" for s in issues[0].suggestions)
    print("  OK  suggestions: unknown word carries suggestions from the source")


def test_empty_and_whitespace_fields():
    issues = review_fields([("f1", ""), ("f2", "   "), ("f3", None or "")],
                           _fake_is_known, _fake_suggest, ignore_words=[])
    assert issues == []
    print("  OK  edge: empty/whitespace fields yield nothing, no crash")


def run_all():
    print("\nSpell-review pipeline Python port -- verification\n" + "-" * 52)
    test_tokenize_spans_are_correct()
    test_blank_tokens_not_flagged()
    test_urls_and_emails_masked()
    test_numeric_and_acronym_excluded()
    test_ignore_list_case_and_space_insensitive()
    test_dedup_collapses_repeats_with_all_occurrences()
    test_known_words_never_flagged()
    test_suggestions_flow_through()
    test_empty_and_whitespace_fields()
    print("-" * 52)
    print("ALL PASS -- tokenization, exclusions, ignore-list, de-dup and the")
    print("dictionary seam are proved. The only unproved piece is Hunspell itself,")
    print("which is App-only and confirmed by the Windows build.\n")


if __name__ == "__main__":
    run_all()
