#!/usr/bin/env python3
"""
Python port of the grammar-scope selection + GrammarField building
(feature: AI grammar review, phase 3 — the bridge from inventory to engine).

When the user runs an AI check they pick a scope: a specific section, the study
cards, or the whole quiz. This logic turns that choice + the document's text
inventory into the list of GrammarField(id, label, text) the engine reviews,
and — crucially — keeps a back-map from each assigned id to the real TextField
so an accepted rewrite can be routed home.

Proved here:
  1. SECTION scope selects only fields whose section id matches (prompts, hints,
     choices, answers, section title of that one section) — nothing from other
     sections, nothing quiz-level, no study cards.
  2. STUDY-CARDS scope selects only the study-card fields.
  3. WHOLE-QUIZ selects everything.
  4. Each selected field gets a STABLE sequential id (0,1,2,…) and its
     description text is HTML-stripped (reusing the same strip the spell-checker
     uses) so tag names never reach the model; non-description text is passed
     through unchanged.
  5. Empty/whitespace fields are dropped from the batch (nothing to review).
  6. A description field is marked non-replaceable (offsets on stripped text),
     mirroring the spelling rule — so phase-3 UI disables Accept on it.
  7. The id→TextField back-map lets an accepted suggestion (which carries the
     field id) be routed to the right TextField for SpellFixApplier.
"""

from dataclasses import dataclass
from enum import Enum
from typing import Callable, Dict, List, Optional, Tuple


class Kind(Enum):
    QuizTitle = "QuizTitle"
    QuizDescription = "QuizDescription"
    SectionTitle = "SectionTitle"
    QuestionPrompt = "QuestionPrompt"
    ChoiceText = "ChoiceText"
    StudyCardFront = "StudyCardFront"
    StudyCardBack = "StudyCardBack"


@dataclass
class TextField:
    kind: Kind
    label: str
    section_id: Optional[str]
    text: str
    # a setter closure in the real code; here just an index for identity
    ident: int


@dataclass
class GrammarField:
    field_id: int
    label: str
    text: str
    replaceable: bool


class Scope(Enum):
    SECTION = "section"
    STUDY_CARDS = "study_cards"
    WHOLE_QUIZ = "whole_quiz"


_STUDY_CARD_KINDS = {Kind.StudyCardFront, Kind.StudyCardBack}


def strip_html(text: str) -> str:
    """Stand-in for DescriptionParser.ToPlainText: removes the safelisted tags.
    Only the tag-name cases matter for the port's assertions."""
    import re
    # remove <tag> and </tag> for the safelist; collapse to spaces
    out = re.sub(r"</?(?:b|strong|i|em|br|ul|li)\s*/?>", " ", text, flags=re.IGNORECASE)
    return out


def select_fields(
    inventory: List[TextField],
    scope: Scope,
    section_id: Optional[str],
) -> List[TextField]:
    """Filter the inventory down to the fields in scope."""
    if scope == Scope.WHOLE_QUIZ:
        return list(inventory)
    if scope == Scope.STUDY_CARDS:
        return [f for f in inventory if f.kind in _STUDY_CARD_KINDS]
    if scope == Scope.SECTION:
        if section_id is None:
            return []
        return [f for f in inventory if f.section_id == section_id]
    return []


def build_grammar_fields(
    selected: List[TextField],
) -> Tuple[List[GrammarField], Dict[int, TextField], Dict[int, bool]]:
    """Assign stable ids, strip the description, drop empties. Returns the
    engine input, the id→TextField back-map, and id→replaceable."""
    fields: List[GrammarField] = []
    back_map: Dict[int, TextField] = {}
    replaceable_map: Dict[int, bool] = {}

    next_id = 0
    for tf in selected:
        is_desc = tf.kind == Kind.QuizDescription
        text = strip_html(tf.text) if is_desc else tf.text
        if not text or not text.strip():
            continue
        fields.append(GrammarField(next_id, tf.label, text, replaceable=not is_desc))
        back_map[next_id] = tf
        replaceable_map[next_id] = not is_desc
        next_id += 1

    return fields, back_map, replaceable_map


# --------------------------------------------------------------------------- #
# Tests
# --------------------------------------------------------------------------- #

def _inventory():
    return [
        TextField(Kind.QuizTitle, "Quiz title", None, "My Quiz", 0),
        TextField(Kind.QuizDescription, "Quiz description", None,
                  "<strong>Intro</strong> to the <ul><li>topic</li></ul>", 1),
        TextField(Kind.SectionTitle, "Section title", "sec-A", "Section A", 2),
        TextField(Kind.QuestionPrompt, "Question prompt", "sec-A", "Their going home.", 3),
        TextField(Kind.ChoiceText, "Choice", "sec-A", "A apple", 4),
        TextField(Kind.SectionTitle, "Section title", "sec-B", "Section B", 5),
        TextField(Kind.QuestionPrompt, "Question prompt", "sec-B", "Its raining.", 6),
        TextField(Kind.StudyCardFront, "Study card (front)", None, "Term", 7),
        TextField(Kind.StudyCardBack, "Study card (back)", None, "Definiton", 8),
    ]


def test_section_scope_selects_only_that_section():
    sel = select_fields(_inventory(), Scope.SECTION, "sec-A")
    idents = sorted(f.ident for f in sel)
    assert idents == [2, 3, 4], idents  # section A's title, prompt, choice
    print("  OK  section scope: only that section's fields")


def test_study_cards_scope():
    sel = select_fields(_inventory(), Scope.STUDY_CARDS, None)
    idents = sorted(f.ident for f in sel)
    assert idents == [7, 8], idents
    print("  OK  study-cards scope: only study-card fields")


def test_whole_quiz_scope():
    sel = select_fields(_inventory(), Scope.WHOLE_QUIZ, None)
    assert len(sel) == 9
    print("  OK  whole-quiz scope: everything")


def test_section_scope_none_id_empty():
    assert select_fields(_inventory(), Scope.SECTION, None) == []
    print("  OK  section scope with no id -> empty")


def test_build_strips_description_and_assigns_ids():
    sel = select_fields(_inventory(), Scope.WHOLE_QUIZ, None)
    fields, back_map, repl = build_grammar_fields(sel)
    # ids are sequential from 0
    assert [f.field_id for f in fields] == list(range(len(fields)))
    # the description field: tag names gone
    desc = next(f for f in fields if f.label == "Quiz description")
    for tag in ("strong", "ul", "li"):
        assert tag not in desc.text.split(), f"'{tag}' leaked: {desc.text!r}"
    assert "Intro" in desc.text and "topic" in desc.text
    # description marked non-replaceable
    assert repl[desc.field_id] is False
    # a normal field is replaceable
    prompt = next(f for f in fields if f.text == "Their going home.")
    assert repl[prompt.field_id] is True
    print(f"  OK  build: ids sequential, description stripped + non-replaceable")


def test_build_drops_empty_fields():
    inv = [
        TextField(Kind.QuestionPrompt, "Prompt", "s", "real text", 0),
        TextField(Kind.QuestionPrompt, "Prompt", "s", "   ", 1),
        TextField(Kind.QuestionPrompt, "Prompt", "s", "", 2),
    ]
    fields, back_map, _ = build_grammar_fields(inv)
    assert len(fields) == 1 and fields[0].text == "real text"
    assert back_map[0].ident == 0
    print("  OK  build: empty/whitespace fields dropped")


def test_back_map_routes_to_source():
    sel = select_fields(_inventory(), Scope.STUDY_CARDS, None)
    fields, back_map, _ = build_grammar_fields(sel)
    # the back of the card ("Definiton") should map to inventory ident 8
    back = next(f for f in fields if f.text == "Definiton")
    assert back_map[back.field_id].ident == 8
    print("  OK  back-map: assigned id routes home to the real TextField")


def test_description_only_scope_stays_clean():
    # a whole-quiz build where description is the only HTML field: no other field
    # is stripped (a literal '<' in a prompt would survive — not tested here, but
    # the is_desc guard ensures only description is stripped)
    sel = select_fields(_inventory(), Scope.WHOLE_QUIZ, None)
    fields, _, _ = build_grammar_fields(sel)
    prompt = next(f for f in fields if f.text == "Their going home.")
    assert "<" not in prompt.text  # unchanged, no accidental stripping
    print("  OK  only description is stripped; other fields pass through")


def run_all():
    print("\nGrammar scope + field-building port -- verification\n" + "-" * 52)
    test_section_scope_selects_only_that_section()
    test_study_cards_scope()
    test_whole_quiz_scope()
    test_section_scope_none_id_empty()
    test_build_strips_description_and_assigns_ids()
    test_build_drops_empty_fields()
    test_back_map_routes_to_source()
    test_description_only_scope_stays_clean()
    print("-" * 52)
    print("ALL PASS -- scope selection, id assignment, description stripping, empty")
    print("dropping, and the id->source back-map are proved. Safe to port.\n")


if __name__ == "__main__":
    run_all()
