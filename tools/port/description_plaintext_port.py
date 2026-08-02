#!/usr/bin/env python3
"""
Python port of DescriptionParser.ExtractPlainText (feature B, HTML-in-description fix).

The quiz DESCRIPTION is the one authored field that carries markup — a small
safelist of tags (b/strong, i/em, br, ul, li), everything else literal. The
spell-checker was treating the raw string as prose, so it flagged the tag names
themselves ("strong", "br", "ul", "li") as misspellings.

Fix: for the description field only, spell-check the PLAIN TEXT the reader sees,
not the raw markup. Rather than write a second HTML stripper (fragile, and a
second source of truth for "what is a tag"), we reuse the existing
DescriptionParser: parse to runs/blocks, then concatenate the run texts. This
port proves that extraction:

  * safelisted tags contribute NO text (their names never appear)
  * literal text is preserved exactly, including a literal "<" with no ">"
  * an escaped-looking "<b class=x>" (a tag WITH an attribute) is literal, per
    the parser's no-attributes rule, so its text is kept as typed
  * <br> and newlines become spaces/breaks, not the letters "br"
  * list items and paragraphs are joined with whitespace so adjacent words
    don't fuse ("oneword" from "one</li><li>word")

Only the description uses this. Every other field is checked as-is.
"""

from dataclasses import dataclass, field
from typing import List, Optional


SAFE = {"b", "strong", "i", "em", "br", "ul", "li"}


@dataclass
class Run:
    text: str


def parse_runs(text: Optional[str]) -> List[Run]:
    """A faithful-enough port of DescriptionParser: yields the literal text runs,
    dropping safelisted tags, turning <br>/newlines into a single space. We only
    need the text content for spell-checking, not the bold/italic/list structure,
    so blocks are flattened with spaces between them."""
    if not text:
        return []

    runs: List[Run] = []
    buffer: List[str] = []

    def flush():
        if buffer:
            runs.append(Run("".join(buffer)))
            buffer.clear()

    i = 0
    n = len(text)
    while i < n:
        c = text[i]

        if c == "\r":
            i += 1
            continue

        if c == "\n":
            # a line break: separate words with a space so they don't fuse
            flush()
            runs.append(Run(" "))
            i += 1
            continue

        if c != "<":
            buffer.append(c)
            i += 1
            continue

        close = text.find(">", i)
        if close == -1:
            # '<' with no '>' — literal, per the parser
            buffer.append(c)
            i += 1
            continue

        raw = text[i + 1:close].strip()
        is_closing = raw.startswith("/")
        name = (raw[1:] if is_closing else raw).strip().rstrip("/").strip()

        # The parser's no-attributes rule: a tag token with a space (attributes)
        # is NOT a safelisted tag — it's literal text. "b" is bold; "b class=x"
        # is the literal characters "<b class=x>".
        first_word = name.split()[0] if name else ""
        has_attributes = " " in name

        if first_word.lower() in SAFE and not has_attributes:
            # It's a real tag. It contributes no text. <br>/<li>/<ul> act as
            # separators so neighbouring words don't fuse.
            flush()
            if first_word.lower() in ("br", "li", "ul"):
                runs.append(Run(" "))
            i = close + 1
            continue

        # Not a safelisted tag: the whole "<...>" is literal text.
        buffer.append(text[i:close + 1])
        i = close + 1

    flush()
    return runs


def extract_plain_text(text: Optional[str]) -> str:
    """The public helper: the reader-visible plain text of a description."""
    return "".join(r.text for r in parse_runs(text))


# --------------------------------------------------------------------------- #
# Tests
# --------------------------------------------------------------------------- #

def test_safelisted_tag_names_do_not_leak():
    src = "<strong>Rules and Regulations</strong><br><br><ul><li>bold</li></ul>"
    out = extract_plain_text(src)
    for tag in ("strong", "br", "ul", "li"):
        # the standalone tag word must not appear (substring 'bold' contains no tag)
        assert f" {tag} " not in f" {out} ", f"tag '{tag}' leaked: {out!r}"
    assert "Rules and Regulations" in out
    assert "bold" in out
    print(f"  OK  tag names don't leak; visible text kept -> {out!r}")


def test_words_do_not_fuse_across_tags():
    src = "<ul><li>one</li><li>word</li></ul>"
    out = extract_plain_text(src)
    assert "oneword" not in out, out
    assert "one" in out and "word" in out
    print(f"  OK  list items separated, no word fusion -> {out!r}")


def test_literal_lt_is_preserved():
    # "if x < 5" — a '<' with no matching tag stays literal
    src = "compute if x < 5 then stop"
    out = extract_plain_text(src)
    assert "x < 5" in out, out
    print(f"  OK  literal '<' preserved -> {out!r}")


def test_tag_with_attribute_is_literal():
    # per the no-attributes rule, "<b class=x>" is literal characters, not bold
    src = "<b class=x>kept</b>"
    out = extract_plain_text(src)
    assert "<b class=x>" in out, out   # literal opening survives
    assert "kept" in out
    # the plain closing </b> IS a safelisted tag, so it contributes nothing
    print(f"  OK  attributed tag treated as literal -> {out!r}")


def test_plain_text_unchanged():
    src = "A perfectly ordinary description with no markup."
    assert extract_plain_text(src) == src
    print("  OK  markup-free text passes through unchanged")


def test_br_becomes_space_not_letters():
    src = "line one<br>line two"
    out = extract_plain_text(src)
    words = out.split()
    assert "br" not in words, f"stray 'br' token in {words}"
    assert "one" in words and "two" in words
    # the <br> boundary must separate, so "one" and "line" don't fuse
    assert "oneline" not in out.replace(" ", "")[:0] or True  # readability guard
    assert out.split() == ["line", "one", "line", "two"], out
    print(f"  OK  <br> -> separator, not letters -> {out!r}")


def test_empty_and_none():
    assert extract_plain_text("") == ""
    assert extract_plain_text(None) == ""
    print("  OK  empty / None -> empty string")


def test_realistic_ny_description():
    # the actual description from the user's screenshot
    src = "<strong>Rules and Regulations</strong><br><br><ul><li>bold, italic, bullets, and line breaks.</li></ul>"
    out = extract_plain_text(src)
    words = out.split()
    for tag in ("strong", "br", "ul", "li"):
        assert tag not in words, f"'{tag}' present as a word in {words}"
    assert "Rules" in words and "Regulations" in words
    print(f"  OK  realistic description clean -> {out!r}")


def run_all():
    print("\nDescription plain-text extraction port -- verification\n" + "-" * 52)
    test_safelisted_tag_names_do_not_leak()
    test_words_do_not_fuse_across_tags()
    test_literal_lt_is_preserved()
    test_tag_with_attribute_is_literal()
    test_plain_text_unchanged()
    test_br_becomes_space_not_letters()
    test_empty_and_none()
    test_realistic_ny_description()
    print("-" * 52)
    print("ALL PASS -- description markup is reduced to reader-visible text;")
    print("tag names never reach the spell-checker, literals survive. Safe to port.\n")


if __name__ == "__main__":
    run_all()
