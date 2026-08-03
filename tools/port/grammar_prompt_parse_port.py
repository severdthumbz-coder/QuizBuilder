#!/usr/bin/env python3
"""
Python port of the grammar-review prompt builder and response parser
(feature: AI grammar review, phase 2 — the provider-agnostic "brain").

The network call itself (HttpClient) is App-only and confirmed by the maintainer.
What IS modelled and proved here is everything around it, which is pure logic and
belongs in Core where it can be unit-tested without a network:

  1. PROMPT BUILDING — turn the scoped, HTML-stripped field texts into a single
     instruction that asks the model for STRUCTURED JSON only. Each field is
     given a stable integer id so a suggestion can be tied back to its field.

  2. RESPONSE PARSING — the hard part. Models do not reliably return bare JSON.
     The parser must survive:
       * a clean JSON array
       * JSON wrapped in ```json ... ``` fences
       * JSON with prose before/after ("Here are the issues: [...] Hope this helps")
       * an object wrapper {"suggestions": [...]} instead of a bare array
       * malformed / truncated JSON  -> a clean parse error, never a crash
       * an empty array               -> "no suggestions", a success not an error
       * suggestions whose "original" text is NOT found in the referenced field
         (a hallucinated span) -> DROPPED, because we cannot anchor a rewrite we
         cannot locate, and applying it would corrupt the text
       * a suggestion referencing an unknown field id -> DROPPED

  3. ANCHORING — for each surviving suggestion, locate the original text within
     its field so the accept step (phase 3) can splice the rewrite at a real
     offset. A suggestion that cannot be anchored is not surfaced.

The output is a list of GrammarSuggestion(field_id, start, length, original,
rewrite, explanation). Only anchored, field-matched suggestions survive.
"""

from dataclasses import dataclass
from typing import List, Optional, Tuple
import json
import re


# --------------------------------------------------------------------------- #
# Inputs / outputs
# --------------------------------------------------------------------------- #

@dataclass
class FieldText:
    """A scoped field's checkable text (already HTML-stripped by the caller)."""
    field_id: int
    label: str          # e.g. "Question prompt" — for context in the prompt
    text: str


@dataclass
class GrammarSuggestion:
    field_id: int
    start: int
    length: int
    original: str
    rewrite: str
    explanation: str


@dataclass
class ParseResult:
    ok: bool
    suggestions: List[GrammarSuggestion]
    error: Optional[str] = None


# --------------------------------------------------------------------------- #
# 1. Prompt building
# --------------------------------------------------------------------------- #

SYSTEM_INSTRUCTION = (
    "You are a careful copy-editor for quiz content. You are given numbered text "
    "fields. Find grammar, spelling, punctuation, and clear phrasing problems. "
    "Do NOT rewrite for style or tone, do not change meaning, and do not flag "
    "correct text. Return ONLY a JSON array, no prose, no markdown. Each element: "
    '{"field": <int>, "original": "<exact substring from that field>", '
    '"rewrite": "<the corrected substring>", "reason": "<short why>"}. '
    "The \"original\" MUST be copied verbatim from the field so it can be located. "
    "If there are no problems, return []."
)


def build_prompt(fields: List[FieldText]) -> str:
    """Compose the user message: the fields, numbered, for the model to review."""
    lines = ["Review these fields:", ""]
    for f in fields:
        # Only include fields that actually have text.
        if f.text and f.text.strip():
            lines.append(f"[{f.field_id}] ({f.label}): {f.text}")
    lines.append("")
    lines.append("Return the JSON array now.")
    return "\n".join(lines)


def has_checkable_text(fields: List[FieldText]) -> bool:
    return any(f.text and f.text.strip() for f in fields)


# --------------------------------------------------------------------------- #
# 2. Response parsing
# --------------------------------------------------------------------------- #

_FENCE_RE = re.compile(r"```(?:json)?\s*(.*?)\s*```", re.DOTALL | re.IGNORECASE)


def _extract_json_text(raw: str) -> Optional[str]:
    """Pull the JSON payload out of a model reply that may be fenced or prose-
    wrapped. Strategy, in order:
      1. a ```json ... ``` fence, if present
      2. the outermost [...] or {...} span in the text
      3. the whole string, trimmed
    Returns None only if nothing bracket-like is present at all."""
    if not raw or not raw.strip():
        return None

    m = _FENCE_RE.search(raw)
    if m:
        return m.group(1).strip()

    # Find the first '[' or '{' and the matching last ']' or '}'.
    starts = [i for i in (raw.find("["), raw.find("{")) if i != -1]
    if starts:
        start = min(starts)
        end = max(raw.rfind("]"), raw.rfind("}"))
        if end > start:
            return raw[start:end + 1].strip()

    return raw.strip()


def _coerce_to_list(parsed) -> Optional[list]:
    """Accept either a bare array or a {"suggestions"/"issues"/...: [...]} wrapper."""
    if isinstance(parsed, list):
        return parsed
    if isinstance(parsed, dict):
        for key in ("suggestions", "issues", "results", "items", "corrections"):
            if isinstance(parsed.get(key), list):
                return parsed[key]
        # a single suggestion object, not wrapped in a list
        if {"field", "original", "rewrite"} <= set(parsed.keys()):
            return [parsed]
    return None


def _anchor(field_text: str, original: str) -> Optional[Tuple[int, int]]:
    """Locate `original` within `field_text`. Exact match first; then a
    whitespace-normalised match (models often normalise runs of spaces). Returns
    (start, length) into the ORIGINAL field_text, or None if unlocatable."""
    if not original:
        return None

    idx = field_text.find(original)
    if idx != -1:
        return idx, len(original)

    # Whitespace-tolerant: build a regex that lets any run of whitespace in the
    # original match any run of whitespace in the field.
    parts = [re.escape(tok) for tok in original.split()]
    if not parts:
        return None
    pattern = r"\s+".join(parts)
    m = re.search(pattern, field_text)
    if m:
        return m.start(), m.end() - m.start()

    return None


def parse_response(raw: str, fields: List[FieldText]) -> ParseResult:
    """Turn a raw model reply into anchored GrammarSuggestions."""
    by_id = {f.field_id: f for f in fields}

    json_text = _extract_json_text(raw)
    if json_text is None:
        return ParseResult(ok=False, suggestions=[], error="Empty response from the model.")

    try:
        parsed = json.loads(json_text)
    except (json.JSONDecodeError, ValueError):
        return ParseResult(
            ok=False, suggestions=[],
            error="The model's response was not valid JSON.")

    items = _coerce_to_list(parsed)
    if items is None:
        return ParseResult(
            ok=False, suggestions=[],
            error="The model's response was not in the expected shape.")

    suggestions: List[GrammarSuggestion] = []
    for item in items:
        if not isinstance(item, dict):
            continue
        fid = item.get("field")
        original = item.get("original")
        rewrite = item.get("rewrite")
        reason = item.get("reason") or item.get("explanation") or ""

        # Field id may arrive as a string; coerce.
        try:
            fid = int(fid)
        except (TypeError, ValueError):
            continue

        if fid not in by_id:
            continue  # unknown field — drop
        if not isinstance(original, str) or not isinstance(rewrite, str):
            continue
        if not original:
            continue
        if original == rewrite:
            continue  # no-op suggestion

        anchor = _anchor(by_id[fid].text, original)
        if anchor is None:
            continue  # hallucinated / unlocatable original — drop

        start, length = anchor
        suggestions.append(GrammarSuggestion(
            field_id=fid, start=start, length=length,
            original=by_id[fid].text[start:start + length],  # exact source span
            rewrite=rewrite,
            explanation=str(reason)))

    return ParseResult(ok=True, suggestions=suggestions)


# --------------------------------------------------------------------------- #
# Tests
# --------------------------------------------------------------------------- #

def _fields():
    return [
        FieldText(0, "Question prompt", "Their going to the store tomorrow."),
        FieldText(1, "Choice", "A apple a day."),
        FieldText(2, "Hint", "This sentence is perfectly fine."),
    ]


def test_prompt_includes_only_nonempty_fields():
    fs = [FieldText(0, "Prompt", "hello"), FieldText(1, "Hint", "   "), FieldText(2, "Choice", "world")]
    p = build_prompt(fs)
    assert "[0]" in p and "[2]" in p
    assert "[1]" not in p, "empty field should be omitted"
    print("  OK  prompt: only non-empty fields included, each with its id")


def test_clean_json_array():
    raw = '[{"field":0,"original":"Their going","rewrite":"They\'re going","reason":"contraction"}]'
    r = parse_response(raw, _fields())
    assert r.ok and len(r.suggestions) == 1
    s = r.suggestions[0]
    assert s.field_id == 0 and s.original == "Their going" and s.rewrite == "They're going"
    assert s.start == 0 and s.length == len("Their going")
    print("  OK  clean JSON array parsed and anchored")


def test_fenced_json():
    raw = '```json\n[{"field":1,"original":"A apple","rewrite":"An apple","reason":"article"}]\n```'
    r = parse_response(raw, _fields())
    assert r.ok and len(r.suggestions) == 1
    assert r.suggestions[0].field_id == 1 and r.suggestions[0].rewrite == "An apple"
    print("  OK  ```json fenced``` payload extracted")


def test_prose_wrapped_json():
    raw = 'Sure! Here are the issues I found:\n[{"field":0,"original":"Their","rewrite":"They\'re","reason":"x"}]\nHope this helps!'
    r = parse_response(raw, _fields())
    assert r.ok and len(r.suggestions) == 1
    assert r.suggestions[0].original == "Their"
    print("  OK  prose-wrapped JSON extracted")


def test_object_wrapper():
    raw = '{"suggestions":[{"field":1,"original":"A apple","rewrite":"An apple","reason":"x"}]}'
    r = parse_response(raw, _fields())
    assert r.ok and len(r.suggestions) == 1
    print("  OK  {suggestions:[...]} wrapper unwrapped")


def test_empty_array_is_success_not_error():
    r = parse_response("[]", _fields())
    assert r.ok and r.suggestions == [] and r.error is None
    print("  OK  empty array -> success with zero suggestions (no error)")


def test_malformed_json_is_clean_error():
    r = parse_response('[{"field":0,"original":"Their",', _fields())
    assert not r.ok and r.suggestions == [] and "JSON" in r.error
    print("  OK  malformed JSON -> clean error, no crash")


def test_hallucinated_original_is_dropped():
    # model claims text that isn't in field 2
    raw = '[{"field":2,"original":"nonexistent phrase","rewrite":"whatever","reason":"x"}]'
    r = parse_response(raw, _fields())
    assert r.ok and r.suggestions == [], "unlocatable original must be dropped"
    print("  OK  hallucinated/unlocatable original dropped, not surfaced")


def test_unknown_field_dropped():
    raw = '[{"field":99,"original":"whatever","rewrite":"x","reason":"y"}]'
    r = parse_response(raw, _fields())
    assert r.ok and r.suggestions == []
    print("  OK  unknown field id dropped")


def test_noop_suggestion_dropped():
    raw = '[{"field":0,"original":"Their going","rewrite":"Their going","reason":"none"}]'
    r = parse_response(raw, _fields())
    assert r.ok and r.suggestions == []
    print("  OK  no-op (original == rewrite) dropped")


def test_whitespace_tolerant_anchor():
    fields = [FieldText(0, "Prompt", "the  quick   brown fox")]  # irregular spaces
    raw = '[{"field":0,"original":"quick brown","rewrite":"swift brown","reason":"x"}]'
    r = parse_response(raw, fields)
    assert r.ok and len(r.suggestions) == 1
    s = r.suggestions[0]
    # anchored to the real (irregularly-spaced) span in the source
    assert fields[0].text[s.start:s.start + s.length] == "quick   brown"
    print("  OK  whitespace-tolerant anchoring maps to the real source span")


def test_string_field_id_coerced():
    raw = '[{"field":"0","original":"Their","rewrite":"They\'re","reason":"x"}]'
    r = parse_response(raw, _fields())
    assert r.ok and len(r.suggestions) == 1 and r.suggestions[0].field_id == 0
    print("  OK  string field id coerced to int")


def test_empty_response_error():
    r = parse_response("", _fields())
    assert not r.ok and "Empty" in r.error
    print("  OK  empty response -> clean error")


def run_all():
    print("\nGrammar prompt/parse port -- verification\n" + "-" * 52)
    test_prompt_includes_only_nonempty_fields()
    test_clean_json_array()
    test_fenced_json()
    test_prose_wrapped_json()
    test_object_wrapper()
    test_empty_array_is_success_not_error()
    test_malformed_json_is_clean_error()
    test_hallucinated_original_is_dropped()
    test_unknown_field_dropped()
    test_noop_suggestion_dropped()
    test_whitespace_tolerant_anchor()
    test_string_field_id_coerced()
    test_empty_response_error()
    print("-" * 52)
    print("ALL PASS -- prompt building and the resilient parser (fences, prose,")
    print("wrappers, malformed, hallucinated spans, anchoring) are proved.")
    print("The network call itself is App-only, confirmed on the maintainer's machine.\n")


if __name__ == "__main__":
    run_all()
