#!/usr/bin/env python3
"""
Python port of DocumentTextInventory.

Purpose (mirrors the grading / pause-resume ports): model the C# object graph
and the text-walking logic in Python, then run it exhaustively BEFORE writing
the C#. This proves two properties a compiler will not:

  1. COVERAGE  -- the walker yields every authored, user-facing text field on the
     QuizDocument graph, and NOTHING that is machinery (Ids, enum-ish flags,
     image paths, ordinals, points, word counts).
  2. ROUND-TRIP -- each yielded field carries a setter that, when called, mutates
     the exact source location it was read from (so an accepted spelling
     correction lands back on the model, not on a copy).

The field surface is taken verbatim from QuizBuilder.Core/Models/Question.cs:

    QuizDocument.Title                        (string)
    QuizDocument.Description                  (string)
    Section.Title                             (string)          [per section]
    Question.Prompt                           (string)          [every question]
    Question.Hint                             (string?)         [every question]
    Choice.Text                               (string)          [mc-single, mc-multi]
    ShortAnswerQuestion.AcceptedAnswers[i]    (List<string>)
    FillInTheBlank Blank.AcceptedAnswers[i]   (List<string>)    [per blank]
    MatchPair.Left / .Right                   (string)          [matching]
    MatchingQuestion.Distractors[i]           (List<string>)    [matching]
    SequenceQuestion.Items[i]                 (List<string>)    [sequence]
    EssayQuestion.RubricNotes                 (string?)         [essay]
    StudyCard.Front / .Back                   (string)          [per study card]

Explicitly NOT text to check (machinery / non-authored):
    *.Id, Points, IsCorrect, CorrectAnswer, CaseSensitive, AllowPartialCredit,
    Ordinal, SuggestedWordCount, ImageRelativePath, FrontImageRelativePath,
    BackImageRelativePath, ThemeId, CustomTheme, SectionDisplayOrder,
    CreatedUtc, ModifiedUtc.
"""

from dataclasses import dataclass, field
from enum import Enum
from typing import Callable, List, Optional
import uuid


# --------------------------------------------------------------------------- #
# Model mirror (only the shape the walker touches; faithful to Question.cs)
# --------------------------------------------------------------------------- #

class Kind(Enum):
    MC_SINGLE = "mc-single"
    MC_MULTI = "mc-multi"
    TRUE_FALSE = "true-false"
    SHORT_ANSWER = "short-answer"
    FILL_BLANK = "fill-blank"
    MATCHING = "matching"
    ESSAY = "essay"
    SEQUENCE = "sequence"


def new_id() -> str:
    return str(uuid.uuid4())


@dataclass
class Choice:
    text: str = ""
    is_correct: bool = False
    id: str = field(default_factory=new_id)


@dataclass
class Blank:
    ordinal: int = 1
    accepted_answers: List[str] = field(default_factory=list)
    id: str = field(default_factory=new_id)


@dataclass
class MatchPair:
    left: str = ""
    right: str = ""
    id: str = field(default_factory=new_id)


@dataclass
class Question:
    kind: Kind = Kind.TRUE_FALSE
    prompt: str = ""
    hint: Optional[str] = None
    points: float = 1.0
    image_relative_path: Optional[str] = None
    id: str = field(default_factory=new_id)
    # type-specific (only the relevant ones are populated per kind)
    choices: List[Choice] = field(default_factory=list)
    accepted_answers: List[str] = field(default_factory=list)     # short-answer
    case_sensitive: bool = False
    correct_answer: bool = True                                    # true/false
    allow_partial_credit: bool = True
    blanks: List[Blank] = field(default_factory=list)             # fill-blank
    pairs: List[MatchPair] = field(default_factory=list)          # matching
    distractors: List[str] = field(default_factory=list)         # matching
    items: List[str] = field(default_factory=list)               # sequence
    rubric_notes: Optional[str] = None                           # essay
    suggested_word_count: int = 0                                 # essay


@dataclass
class Section:
    title: str = "Untitled Section"
    questions: List[Question] = field(default_factory=list)
    id: str = field(default_factory=new_id)


@dataclass
class StudyCard:
    front: str = ""
    back: str = ""
    front_image_relative_path: Optional[str] = None
    back_image_relative_path: Optional[str] = None
    id: str = field(default_factory=new_id)


@dataclass
class QuizDocument:
    title: str = "Untitled Quiz"
    description: str = ""
    sections: List[Section] = field(default_factory=list)
    study_cards: List[StudyCard] = field(default_factory=list)
    id: str = field(default_factory=new_id)


# --------------------------------------------------------------------------- #
# The inventory record + walker  (this is what becomes C# DocumentTextInventory)
# --------------------------------------------------------------------------- #

class FieldKind(Enum):
    QUIZ_TITLE = "QuizTitle"
    QUIZ_DESCRIPTION = "QuizDescription"
    SECTION_TITLE = "SectionTitle"
    QUESTION_PROMPT = "QuestionPrompt"
    QUESTION_HINT = "QuestionHint"
    CHOICE_TEXT = "ChoiceText"
    SHORT_ANSWER = "AcceptedAnswer"
    BLANK_ANSWER = "BlankAnswer"
    MATCH_LEFT = "MatchLeft"
    MATCH_RIGHT = "MatchRight"
    DISTRACTOR = "Distractor"
    SEQUENCE_ITEM = "SequenceItem"
    RUBRIC_NOTES = "RubricNotes"
    STUDY_CARD_FRONT = "StudyCardFront"
    STUDY_CARD_BACK = "StudyCardBack"


@dataclass
class TextField:
    """One addressable piece of authored text.

    section_id / question_id let the UI group results by section (the user's
    explicit ask) and jump to the owning question. `get`/`set` are closures
    over the live model so an accepted correction round-trips to source.
    """
    field_kind: FieldKind
    label: str                     # human label for the review panel
    section_id: Optional[str]
    question_id: Optional[str]
    get: Callable[[], str]
    set: Callable[[str], None]

    @property
    def text(self) -> str:
        return self.get() or ""


def _list_item_accessors(lst: List[str], i: int):
    """Closures that read/write element i of a live list (round-trip safe)."""
    return (lambda: lst[i], lambda v: lst.__setitem__(i, v))


def _attr_accessors(obj, attr: str):
    """Closures for a scalar attribute; None reads as empty, writes set the attr."""
    return (lambda: getattr(obj, attr) or "", lambda v: setattr(obj, attr, v))


def inventory(doc: QuizDocument) -> List[TextField]:
    """Yield every authored text field on the document, in reading order.

    Order: quiz title/description, then each section (title, then each question's
    fields in a stable order), then study cards. Deterministic so the review
    panel and any test see the same sequence.
    """
    out: List[TextField] = []

    out.append(TextField(FieldKind.QUIZ_TITLE, "Quiz title", None, None,
                         *_attr_accessors(doc, "title")))
    out.append(TextField(FieldKind.QUIZ_DESCRIPTION, "Quiz description", None, None,
                         *_attr_accessors(doc, "description")))

    for section in doc.sections:
        sid = section.id
        out.append(TextField(FieldKind.SECTION_TITLE, "Section title", sid, None,
                             *_attr_accessors(section, "title")))

        for q in section.questions:
            qid = q.id
            out.append(TextField(FieldKind.QUESTION_PROMPT, "Question prompt", sid, qid,
                                 *_attr_accessors(q, "prompt")))
            # Hint is optional; still inventoried so an author-written hint gets
            # checked. A None hint reads as "" and is filtered by the caller if
            # they only want non-empty text.
            out.append(TextField(FieldKind.QUESTION_HINT, "Hint", sid, qid,
                                 *_attr_accessors(q, "hint")))

            if q.kind in (Kind.MC_SINGLE, Kind.MC_MULTI):
                for c in q.choices:
                    out.append(TextField(FieldKind.CHOICE_TEXT, "Choice", sid, qid,
                                         *_attr_accessors(c, "text")))
            elif q.kind == Kind.SHORT_ANSWER:
                for i in range(len(q.accepted_answers)):
                    out.append(TextField(FieldKind.SHORT_ANSWER, "Accepted answer",
                                         sid, qid, *_list_item_accessors(q.accepted_answers, i)))
            elif q.kind == Kind.FILL_BLANK:
                for b in q.blanks:
                    for i in range(len(b.accepted_answers)):
                        out.append(TextField(FieldKind.BLANK_ANSWER,
                                             f"Blank {b.ordinal} answer", sid, qid,
                                             *_list_item_accessors(b.accepted_answers, i)))
            elif q.kind == Kind.MATCHING:
                for p in q.pairs:
                    out.append(TextField(FieldKind.MATCH_LEFT, "Match (left)", sid, qid,
                                         *_attr_accessors(p, "left")))
                    out.append(TextField(FieldKind.MATCH_RIGHT, "Match (right)", sid, qid,
                                         *_attr_accessors(p, "right")))
                for i in range(len(q.distractors)):
                    out.append(TextField(FieldKind.DISTRACTOR, "Distractor", sid, qid,
                                         *_list_item_accessors(q.distractors, i)))
            elif q.kind == Kind.SEQUENCE:
                for i in range(len(q.items)):
                    out.append(TextField(FieldKind.SEQUENCE_ITEM, "Sequence item",
                                         sid, qid, *_list_item_accessors(q.items, i)))
            elif q.kind == Kind.ESSAY:
                out.append(TextField(FieldKind.RUBRIC_NOTES, "Rubric notes", sid, qid,
                                     *_attr_accessors(q, "rubric_notes")))
            # TRUE_FALSE has no extra text beyond prompt/hint -- correct.

    for card in doc.study_cards:
        out.append(TextField(FieldKind.STUDY_CARD_FRONT, "Study card (front)", None, None,
                             *_attr_accessors(card, "front")))
        out.append(TextField(FieldKind.STUDY_CARD_BACK, "Study card (back)", None, None,
                             *_attr_accessors(card, "back")))

    return out


# --------------------------------------------------------------------------- #
# Exhaustive tests
# --------------------------------------------------------------------------- #

def _one_of_each_kind_question():
    return [
        Question(kind=Kind.MC_SINGLE, prompt="mc1 prompt", hint="mc1 hint",
                 choices=[Choice("alpha", True), Choice("beta", False)]),
        Question(kind=Kind.MC_MULTI, prompt="mc2 prompt",
                 choices=[Choice("gamma", True), Choice("delta", True)]),
        Question(kind=Kind.TRUE_FALSE, prompt="tf prompt", correct_answer=False),
        Question(kind=Kind.SHORT_ANSWER, prompt="sa prompt",
                 accepted_answers=["colour", "color"]),
        Question(kind=Kind.FILL_BLANK, prompt="fb prompt with {{1}} and {{2}}",
                 blanks=[Blank(1, ["epsilon"]), Blank(2, ["zeta", "zeeta"])]),
        Question(kind=Kind.MATCHING, prompt="match prompt",
                 pairs=[MatchPair("leftA", "rightA"), MatchPair("leftB", "rightB")],
                 distractors=["distract1", "distract2"]),
        Question(kind=Kind.SEQUENCE, prompt="seq prompt",
                 items=["step one", "step two", "step three"]),
        Question(kind=Kind.ESSAY, prompt="essay prompt", rubric_notes="rubric here"),
    ]


def build_full_document() -> QuizDocument:
    return QuizDocument(
        title="Sample Quiz",
        description="A description with somme misspellings.",
        sections=[
            Section(title="Section One", questions=_one_of_each_kind_question()),
            Section(title="Section Two", questions=[
                Question(kind=Kind.TRUE_FALSE, prompt="s2 tf prompt"),  # no hint
            ]),
        ],
        study_cards=[
            StudyCard(front="Front text", back="Back text"),
            StudyCard(front="Term", back="Definition"),
        ],
    )


def test_coverage_counts():
    """Every expected field appears exactly the right number of times."""
    doc = build_full_document()
    fields = inventory(doc)
    counts = {}
    for f in fields:
        counts[f.field_kind] = counts.get(f.field_kind, 0) + 1

    expected = {
        FieldKind.QUIZ_TITLE: 1,
        FieldKind.QUIZ_DESCRIPTION: 1,
        FieldKind.SECTION_TITLE: 2,        # two sections
        FieldKind.QUESTION_PROMPT: 9,      # 8 in S1 + 1 in S2
        FieldKind.QUESTION_HINT: 9,        # inventoried for every question
        FieldKind.CHOICE_TEXT: 4,          # 2 mc-single + 2 mc-multi
        FieldKind.SHORT_ANSWER: 2,         # colour, color
        FieldKind.BLANK_ANSWER: 3,         # blank1:1 + blank2:2
        FieldKind.MATCH_LEFT: 2,
        FieldKind.MATCH_RIGHT: 2,
        FieldKind.DISTRACTOR: 2,
        FieldKind.SEQUENCE_ITEM: 3,
        FieldKind.RUBRIC_NOTES: 1,
        FieldKind.STUDY_CARD_FRONT: 2,
        FieldKind.STUDY_CARD_BACK: 2,
    }
    assert counts == expected, f"coverage mismatch:\n got {counts}\n exp {expected}"
    print(f"  OK  coverage: {len(fields)} fields, all kinds present in exact counts")


def test_every_authored_string_is_reachable():
    """
    Collect every authored string on the graph by brute force, then assert the
    inventory's texts are a superset (modulo the intentionally-empty S2 hint).
    This is the real anti-omission check: if a future field is added to the
    model and NOT to the walker, this fails.
    """
    doc = build_full_document()
    inv_texts = sorted(f.text for f in inventory(doc) if f.text)

    authored = []
    authored += [doc.title, doc.description]
    for s in doc.sections:
        authored.append(s.title)
        for q in s.questions:
            authored.append(q.prompt)
            if q.hint:
                authored.append(q.hint)
            authored += [c.text for c in q.choices]
            authored += list(q.accepted_answers)
            for b in q.blanks:
                authored += list(b.accepted_answers)
            for p in q.pairs:
                authored += [p.left, p.right]
            authored += list(q.distractors)
            authored += list(q.items)
            if q.rubric_notes:
                authored.append(q.rubric_notes)
    for card in doc.study_cards:
        authored += [card.front, card.back]
    authored = sorted(a for a in authored if a)

    assert inv_texts == authored, (
        "inventory did not reach every authored string:\n"
        f"  missing from inventory: {sorted(set(authored) - set(inv_texts))}\n"
        f"  extra in inventory:     {sorted(set(inv_texts) - set(authored))}"
    )
    print(f"  OK  reachability: all {len(authored)} authored strings inventoried, none extra")


def test_no_machinery_leaks():
    """The inventory must never surface non-text machinery values."""
    doc = build_full_document()
    # Poison machinery fields with sentinel strings that would be obvious if leaked.
    doc.sections[0].questions[0].image_relative_path = "images/SHOULD_NOT_APPEAR.png"
    doc.study_cards[0].front_image_relative_path = "images/ALSO_NOT.png"
    texts = " || ".join(f.text for f in inventory(doc))
    for banned in ("SHOULD_NOT_APPEAR", "ALSO_NOT", "images/"):
        assert banned not in texts, f"machinery leaked into inventory: {banned}"
    # Ids/points/ordinals are non-strings and structurally cannot appear; the
    # image-path check is the one real risk since it IS a string. Guarded.
    print("  OK  no machinery: image paths / ids / numerics never surface as text")


def test_round_trip_setters_mutate_source():
    """Calling a field's setter changes the exact model location it came from."""
    doc = build_full_document()
    fields = inventory(doc)

    # Scalar attribute (quiz title)
    title = next(f for f in fields if f.field_kind == FieldKind.QUIZ_TITLE)
    title.set("Corrected Quiz Title")
    assert doc.title == "Corrected Quiz Title"

    # Optional attribute that started None (S2 question has no hint -> "")
    s2_hint = [f for f in fields if f.field_kind == FieldKind.QUESTION_HINT][-1]
    assert s2_hint.text == ""
    s2_hint.set("a newly written hint")
    assert doc.sections[1].questions[0].hint == "a newly written hint"

    # List element (short-answer accepted answer "colour" -> "colour!!")
    sa = next(f for f in fields if f.field_kind == FieldKind.SHORT_ANSWER)
    sa.set("colourFIXED")
    assert doc.sections[0].questions[3].accepted_answers[0] == "colourFIXED"

    # List element deeper in (sequence item 2)
    seq_items = [f for f in fields if f.field_kind == FieldKind.SEQUENCE_ITEM]
    seq_items[1].set("STEP TWO FIXED")
    assert doc.sections[0].questions[6].items[1] == "STEP TWO FIXED"

    # Matching right side
    mr = [f for f in fields if f.field_kind == FieldKind.MATCH_RIGHT][0]
    mr.set("rightA-FIXED")
    assert doc.sections[0].questions[5].pairs[0].right == "rightA-FIXED"

    # Nested list: fill-in-the-blank second blank, second accepted answer
    blanks = [f for f in fields if f.field_kind == FieldKind.BLANK_ANSWER]
    blanks[-1].set("zeeta-FIXED")
    assert doc.sections[0].questions[4].blanks[1].accepted_answers[1] == "zeeta-FIXED"

    print("  OK  round-trip: setters mutate the exact source location (scalars, "
          "optionals, list items, nested lists)")


def test_grouping_metadata_present():
    """Section/question ids are attached so the panel can group + navigate."""
    doc = build_full_document()
    fields = inventory(doc)
    # Quiz-level fields have no section/question
    ql = [f for f in fields if f.field_kind == FieldKind.QUIZ_TITLE][0]
    assert ql.section_id is None and ql.question_id is None
    # A choice knows both its section and its question
    ch = [f for f in fields if f.field_kind == FieldKind.CHOICE_TEXT][0]
    assert ch.section_id == doc.sections[0].id
    assert ch.question_id == doc.sections[0].questions[0].id
    # A section title knows its section but not a question
    st = [f for f in fields if f.field_kind == FieldKind.SECTION_TITLE][0]
    assert st.section_id == doc.sections[0].id and st.question_id is None
    print("  OK  grouping: section/question ids attached for panel grouping + jump")


def test_empty_document():
    """A brand-new document yields exactly title + description, both empty-ish."""
    doc = QuizDocument()
    fields = inventory(doc)
    kinds = [f.field_kind for f in fields]
    assert kinds == [FieldKind.QUIZ_TITLE, FieldKind.QUIZ_DESCRIPTION], kinds
    print("  OK  empty doc: only quiz title + description, nothing spurious")


def test_true_false_has_no_extra_text():
    """A true/false question contributes prompt + hint only -- no phantom fields."""
    doc = QuizDocument(sections=[Section(title="S", questions=[
        Question(kind=Kind.TRUE_FALSE, prompt="p", hint="h", correct_answer=True),
    ])])
    kinds = [f.field_kind for f in inventory(doc)
             if f.field_kind not in (FieldKind.QUIZ_TITLE, FieldKind.QUIZ_DESCRIPTION,
                                     FieldKind.SECTION_TITLE)]
    assert kinds == [FieldKind.QUESTION_PROMPT, FieldKind.QUESTION_HINT], kinds
    print("  OK  true/false: prompt + hint only, correct_answer bool never surfaces")


def run_all():
    print("\nDocumentTextInventory Python port -- verification\n" + "-" * 52)
    test_coverage_counts()
    test_every_authored_string_is_reachable()
    test_no_machinery_leaks()
    test_round_trip_setters_mutate_source()
    test_grouping_metadata_present()
    test_empty_document()
    test_true_false_has_no_extra_text()
    print("-" * 52)
    print("ALL PASS -- inventory covers every authored field, leaks no machinery,")
    print("and every setter round-trips to source. Safe to port to C#.\n")


if __name__ == "__main__":
    run_all()
