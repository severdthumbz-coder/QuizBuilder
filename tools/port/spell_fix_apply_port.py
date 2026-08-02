#!/usr/bin/env python3
"""
Python port of SpellFixApplier's routing (feature B, UI increment).

The review panel's "Replace" must not poke the model raw — the correction has to
land in undo + dirty-tracking. The undo protocol (confirmed from UndoService) is:

    1. undo.CaptureBeforeChange(label)   # snapshot BEFORE mutating
    2. <mutate the model>
    3. <call the IQuizDocumentService method that raises DocumentChanged>

Step 3's method depends on WHICH text field was edited. This port pins that
mapping so the C# switch is proved before it is written:

    QuizTitle          -> SetTitle(newValue)                (raw set not needed;
                                                             service sets it)
    QuizDescription    -> SetDescription(newValue)          (service sets it)
    SectionTitle       -> RenameSection(sectionId, newVal)  (service sets it)
    StudyCardFront/Back-> raw set, then UpdateStudyCard(cardId, front, back)
    everything else
    (inside a question) -> raw set via TextField.Set, then
                           NotifyQuestionChanged(sectionId, questionId)

Key correctness points proved here:
  * The three "service sets it" kinds (title/description/section) must NOT also
    do a raw TextField.Set — the service method is the write. Doing both is
    harmless for title/description but for a section would double-apply; we route
    through the service ONLY.
  * Question-internal kinds have no dedicated service setter, so they DO raw-set
    then Notify. Every such kind must carry a non-null section+question id or the
    Notify cannot be addressed — asserted here.
  * Study cards need both front and back to call UpdateStudyCard, so the applier
    reads the sibling side from the live card. Modelled.
  * After any apply, the review must be RE-RUN: undo/RestoreDocument swaps the
    whole document, so previously-captured field closures go stale. This port
    encodes "apply returns a signal to re-run" rather than reusing old closures.
"""

from dataclasses import dataclass, field
from enum import Enum
from typing import List, Optional, Tuple


class Kind(Enum):
    QuizTitle = "QuizTitle"
    QuizDescription = "QuizDescription"
    SectionTitle = "SectionTitle"
    QuestionPrompt = "QuestionPrompt"
    QuestionHint = "QuestionHint"
    ChoiceText = "ChoiceText"
    AcceptedAnswer = "AcceptedAnswer"
    BlankAnswer = "BlankAnswer"
    MatchLeft = "MatchLeft"
    MatchRight = "MatchRight"
    Distractor = "Distractor"
    SequenceItem = "SequenceItem"
    RubricNotes = "RubricNotes"
    StudyCardFront = "StudyCardFront"
    StudyCardBack = "StudyCardBack"


# The three kinds whose write goes THROUGH a dedicated service setter (no raw set).
_SERVICE_SETTER_KINDS = {Kind.QuizTitle, Kind.QuizDescription, Kind.SectionTitle}

# Study-card kinds need the sibling side to call UpdateStudyCard.
_STUDY_CARD_KINDS = {Kind.StudyCardFront, Kind.StudyCardBack}

# Everything else lives inside a question and routes via NotifyQuestionChanged.
_QUESTION_INTERNAL_KINDS = {
    Kind.QuestionPrompt, Kind.QuestionHint, Kind.ChoiceText, Kind.AcceptedAnswer,
    Kind.BlankAnswer, Kind.MatchLeft, Kind.MatchRight, Kind.Distractor,
    Kind.SequenceItem, Kind.RubricNotes,
}


# --------------------------------------------------------------------------- #
# Fakes: a document service + undo that record calls, so we can assert routing
# --------------------------------------------------------------------------- #

@dataclass
class Call:
    method: str
    args: tuple


class FakeDoc:
    def __init__(self):
        self.calls: List[Call] = []
        self.raw_sets: List[Tuple[str, str]] = []  # (field_key, value)
        # live study-card store for the sibling-read case
        self.cards = {}  # cardId -> {"front":..., "back":...}

    # service methods
    def SetTitle(self, v): self.calls.append(Call("SetTitle", (v,)))
    def SetDescription(self, v): self.calls.append(Call("SetDescription", (v,)))
    def RenameSection(self, sid, v): self.calls.append(Call("RenameSection", (sid, v)))
    def UpdateStudyCard(self, cid, front, back):
        self.calls.append(Call("UpdateStudyCard", (cid, front, back)))
        self.cards[cid] = {"front": front, "back": back}
    def NotifyQuestionChanged(self, sid, qid):
        self.calls.append(Call("NotifyQuestionChanged", (sid, qid)))


class FakeUndo:
    def __init__(self): self.captures: List[str] = []
    def CaptureBeforeChange(self, label): self.captures.append(label)


@dataclass
class FakeField:
    """Stand-in for a TextField: knows its kind, ids, and can raw-set."""
    kind: Kind
    section_id: Optional[str]
    question_id: Optional[str]
    card_id: Optional[str]      # only for study-card kinds
    card_side: Optional[str]    # "front"/"back" for study-card kinds
    value: str = ""
    def get(self): return self.value
    def set(self, v):
        self.value = v


# --------------------------------------------------------------------------- #
# The applier under test
# --------------------------------------------------------------------------- #

APPLY_LABEL = "Spelling correction"


def apply_fix(doc: FakeDoc, undo: FakeUndo, fld: FakeField, replacement: str) -> bool:
    """Apply `replacement` to `fld`, routing through undo + the correct service
    call. Returns True (a signal that the review must be re-run against the now-
    current document, because undo snapshots and closures may be invalidated)."""
    # 1. snapshot BEFORE the mutation
    undo.CaptureBeforeChange(APPLY_LABEL)

    # 2/3. route by kind
    if fld.kind == Kind.QuizTitle:
        doc.SetTitle(replacement)
    elif fld.kind == Kind.QuizDescription:
        doc.SetDescription(replacement)
    elif fld.kind == Kind.SectionTitle:
        assert fld.section_id is not None, "section title needs a section id"
        doc.RenameSection(fld.section_id, replacement)
    elif fld.kind in _STUDY_CARD_KINDS:
        assert fld.card_id is not None, "study card needs a card id"
        # Do NOT raw-set the side first: UpdateStudyCard has a no-op guard that
        # compares against the live card, so a pre-write would make it see no
        # change and skip the notification. Compute both sides and let
        # UpdateStudyCard perform the write.
        card = doc.cards.get(fld.card_id, {"front": "", "back": ""})
        if fld.card_side == "front":
            doc.UpdateStudyCard(fld.card_id, replacement, card["back"])
        else:
            doc.UpdateStudyCard(fld.card_id, card["front"], replacement)
    else:
        # question-internal
        assert fld.kind in _QUESTION_INTERNAL_KINDS, f"unrouted kind {fld.kind}"
        assert fld.section_id is not None and fld.question_id is not None, \
            f"{fld.kind} must carry section+question ids"
        fld.set(replacement)  # raw-set the model
        doc.NotifyQuestionChanged(fld.section_id, fld.question_id)

    return True


# --------------------------------------------------------------------------- #
# Tests
# --------------------------------------------------------------------------- #

def test_every_kind_is_routed():
    """No TextFieldKind falls through unrouted."""
    routed = _SERVICE_SETTER_KINDS | _STUDY_CARD_KINDS | _QUESTION_INTERNAL_KINDS
    assert routed == set(Kind), f"unrouted kinds: {set(Kind) - routed}"
    print("  OK  every TextFieldKind has a routing branch")


def test_title_routes_through_service_only():
    doc, undo = FakeDoc(), FakeUndo()
    f = FakeField(Kind.QuizTitle, None, None, None, None, value="Titel")
    apply_fix(doc, undo, f, "Title")
    assert undo.captures == [APPLY_LABEL]
    assert doc.calls == [Call("SetTitle", ("Title",))]
    print("  OK  quiz title -> CaptureBeforeChange + SetTitle, no raw set")


def test_description_routes_through_service():
    doc, undo = FakeDoc(), FakeUndo()
    f = FakeField(Kind.QuizDescription, None, None, None, None)
    apply_fix(doc, undo, f, "fixed")
    assert doc.calls == [Call("SetDescription", ("fixed",))]
    print("  OK  quiz description -> SetDescription")


def test_section_title_routes_with_id():
    doc, undo = FakeDoc(), FakeUndo()
    f = FakeField(Kind.SectionTitle, "sec-1", None, None, None)
    apply_fix(doc, undo, f, "Chapter One")
    assert doc.calls == [Call("RenameSection", ("sec-1", "Chapter One"))]
    print("  OK  section title -> RenameSection(sectionId, value)")


def test_question_internal_raw_set_then_notify():
    for kind in _QUESTION_INTERNAL_KINDS:
        doc, undo = FakeDoc(), FakeUndo()
        f = FakeField(kind, "sec-1", "q-9", None, None, value="wrng")
        apply_fix(doc, undo, f, "wrong")
        assert f.value == "wrong", f"{kind}: raw set didn't happen"
        assert doc.calls == [Call("NotifyQuestionChanged", ("sec-1", "q-9"))], \
            f"{kind}: expected NotifyQuestionChanged, got {doc.calls}"
    print(f"  OK  all {len(_QUESTION_INTERNAL_KINDS)} question-internal kinds -> raw set + NotifyQuestionChanged")


def test_study_card_reads_sibling_side():
    doc, undo = FakeDoc(), FakeUndo()
    # seed the live card so the sibling read has content
    doc.cards["card-1"] = {"front": "Teh term", "back": "the definition"}
    f = FakeField(Kind.StudyCardFront, None, None, "card-1", "front", value="Teh term")
    apply_fix(doc, undo, f, "The term")
    assert doc.calls == [Call("UpdateStudyCard", ("card-1", "The term", "the definition"))], doc.calls
    print("  OK  study card front -> UpdateStudyCard(card, newFront, existingBack)")


def test_study_card_back_side():
    doc, undo = FakeDoc(), FakeUndo()
    doc.cards["card-2"] = {"front": "Question", "back": "answr"}
    f = FakeField(Kind.StudyCardBack, None, None, "card-2", "back", value="answr")
    apply_fix(doc, undo, f, "answer")
    assert doc.calls == [Call("UpdateStudyCard", ("card-2", "Question", "answer"))]
    print("  OK  study card back -> UpdateStudyCard(card, existingFront, newBack)")


def test_missing_ids_are_caught():
    doc, undo = FakeDoc(), FakeUndo()
    # a question-internal kind with no ids must fail the assertion (would be an
    # un-addressable Notify) rather than silently no-op
    f = FakeField(Kind.ChoiceText, None, None, None, None)
    try:
        apply_fix(doc, undo, f, "x")
        assert False, "expected assertion for missing ids"
    except AssertionError as e:
        assert "section+question" in str(e)
    print("  OK  question-internal kind without ids is rejected, not silently dropped")


def test_apply_signals_rerun():
    doc, undo = FakeDoc(), FakeUndo()
    f = FakeField(Kind.QuizTitle, None, None, None, None)
    assert apply_fix(doc, undo, f, "x") is True
    print("  OK  apply returns re-run signal (closures/undo may be stale after)")


def run_all():
    print("\nSpellFixApplier routing Python port -- verification\n" + "-" * 52)
    test_every_kind_is_routed()
    test_title_routes_through_service_only()
    test_description_routes_through_service()
    test_section_title_routes_with_id()
    test_question_internal_raw_set_then_notify()
    test_study_card_reads_sibling_side()
    test_study_card_back_side()
    test_missing_ids_are_caught()
    test_apply_signals_rerun()
    print("-" * 52)
    print("ALL PASS -- every field kind routes to the right service call, through")
    print("undo, with ids validated and study-card siblings preserved. Safe to port.\n")


if __name__ == "__main__":
    run_all()
