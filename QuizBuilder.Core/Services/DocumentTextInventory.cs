using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <summary>
/// The kind of authored text a <see cref="TextField"/> addresses. Used by the
/// review UI to label a finding and to decide how to group it.
/// </summary>
public enum TextFieldKind
{
    QuizTitle,
    QuizDescription,
    SectionTitle,
    QuestionPrompt,
    QuestionHint,
    ChoiceText,
    AcceptedAnswer,
    BlankAnswer,
    MatchLeft,
    MatchRight,
    Distractor,
    SequenceItem,
    RubricNotes,
    StudyCardFront,
    StudyCardBack,
}

/// <summary>
/// One addressable piece of authored, user-facing text on a <see cref="QuizDocument"/>.
///
/// <para>
/// <see cref="SectionId"/> and <see cref="QuestionId"/> let the review panel
/// group findings by section (the primary UX requirement) and jump to the
/// owning question. Quiz-level fields (title, description, study cards) carry
/// null for both.
/// </para>
/// <para>
/// <see cref="Get"/> and <see cref="Set"/> close over the live model, so an
/// accepted correction round-trips to the exact source location — a scalar
/// property, an optional property, or an element of a <c>List&lt;string&gt;</c>.
/// The setter here writes the raw model. Callers that need undo / autosave /
/// change-notification (the App layer) should route the accepted value through
/// <c>IQuizDocumentService</c> rather than calling <see cref="Set"/> directly;
/// this type deliberately has no dependency on that service so it stays a pure,
/// testable function of the document.
/// </para>
/// </summary>
public sealed class TextField
{
    public TextField(
        TextFieldKind kind,
        string label,
        Guid? sectionId,
        Guid? questionId,
        Func<string> get,
        Action<string> set,
        Guid? ownerId = null)
    {
        Kind = kind;
        Label = label;
        SectionId = sectionId;
        QuestionId = questionId;
        Get = get;
        Set = set;
        OwnerId = ownerId;
    }

    public TextFieldKind Kind { get; }

    /// <summary>Human-readable label for the review panel, e.g. "Question prompt".</summary>
    public string Label { get; }

    public Guid? SectionId { get; }

    public Guid? QuestionId { get; }

    /// <summary>
    /// Id of a non-section, non-question owner the text belongs to — currently a
    /// study card (front/back). Null for quiz-level and question-internal text.
    /// Lets a corrector address the owning object (e.g. call UpdateStudyCard)
    /// without the review layer having to re-scan the document to find it.
    /// </summary>
    public Guid? OwnerId { get; }

    /// <summary>Reads the current text. A null underlying value reads as "".</summary>
    public Func<string> Get { get; }

    /// <summary>Writes the corrected text back to the source location.</summary>
    public Action<string> Set { get; }

    /// <summary>Convenience: the current text (never null).</summary>
    public string Text => Get() ?? string.Empty;
}

/// <summary>
/// Walks a <see cref="QuizDocument"/> and yields every authored, user-facing
/// text field as a <see cref="TextField"/> with a read/write accessor pair.
///
/// <para>
/// This is the single source of truth for "what text exists on a quiz and how
/// to address it," shared by the offline spell-checker and any future text
/// reviewer (e.g. an AI grammar pass). It is pure Core: no WPF, no UI, no
/// dictionary or network dependency, so it is unit-testable against Core alone.
/// </para>
/// <para>
/// Design was proved out in <c>tools/port/text_inventory_port.py</c> before
/// being written here: coverage (no authored field missed), no-machinery-leak
/// (ids, points, ordinals, image paths never surface), and round-trip (every
/// setter mutates its exact source) are all pinned by tests.
/// </para>
/// <para>
/// Ordering is deterministic (reading order): quiz title, quiz description,
/// then each section (title, then each question's fields in a stable per-kind
/// order), then study cards. The <see cref="TextFieldKind.QuestionHint"/> field
/// is emitted for every question even when empty, so an author-written hint is
/// checked; callers wanting only non-empty text filter on <see cref="TextField.Text"/>.
/// </para>
/// </summary>
public static class DocumentTextInventory
{
    /// <summary>
    /// Yields every authored text field on <paramref name="document"/> in
    /// deterministic reading order.
    /// </summary>
    public static IReadOnlyList<TextField> Enumerate(QuizDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var fields = new List<TextField>();

        fields.Add(new TextField(
            TextFieldKind.QuizTitle, "Quiz title", null, null,
            () => document.Title ?? string.Empty,
            v => document.Title = v));

        fields.Add(new TextField(
            TextFieldKind.QuizDescription, "Quiz description", null, null,
            () => document.Description ?? string.Empty,
            v => document.Description = v));

        foreach (var section in document.Sections)
        {
            Guid sid = section.Id;

            fields.Add(new TextField(
                TextFieldKind.SectionTitle, "Section title", sid, null,
                () => section.Title ?? string.Empty,
                v => section.Title = v));

            foreach (var question in section.Questions)
            {
                Guid qid = question.Id;

                fields.Add(new TextField(
                    TextFieldKind.QuestionPrompt, "Question prompt", sid, qid,
                    () => question.Prompt ?? string.Empty,
                    v => question.Prompt = v));

                fields.Add(new TextField(
                    TextFieldKind.QuestionHint, "Hint", sid, qid,
                    () => question.Hint ?? string.Empty,
                    v => question.Hint = v));

                AddTypeSpecific(fields, sid, qid, question);
            }
        }

        foreach (var card in document.StudyCards)
        {
            var c = card; // capture per-iteration
            fields.Add(new TextField(
                TextFieldKind.StudyCardFront, "Study card (front)", null, null,
                () => c.Front ?? string.Empty,
                v => c.Front = v,
                ownerId: c.Id));

            fields.Add(new TextField(
                TextFieldKind.StudyCardBack, "Study card (back)", null, null,
                () => c.Back ?? string.Empty,
                v => c.Back = v,
                ownerId: c.Id));
        }

        return fields;
    }

    private static void AddTypeSpecific(List<TextField> fields, Guid sid, Guid qid, Question question)
    {
        switch (question)
        {
            case MultipleChoiceSingleQuestion mcs:
                AddChoices(fields, sid, qid, mcs.Choices);
                break;

            case MultipleChoiceMultipleQuestion mcm:
                AddChoices(fields, sid, qid, mcm.Choices);
                break;

            case ShortAnswerQuestion sa:
                AddStringList(fields, sid, qid, TextFieldKind.AcceptedAnswer,
                    "Accepted answer", sa.AcceptedAnswers);
                break;

            case FillInTheBlankQuestion fb:
                foreach (var blank in fb.Blanks)
                {
                    AddStringList(fields, sid, qid, TextFieldKind.BlankAnswer,
                        $"Blank {blank.Ordinal} answer", blank.AcceptedAnswers);
                }
                break;

            case MatchingQuestion mq:
                foreach (var pair in mq.Pairs)
                {
                    // Capture the pair reference so the closures write this pair.
                    var p = pair;
                    fields.Add(new TextField(
                        TextFieldKind.MatchLeft, "Match (left)", sid, qid,
                        () => p.Left ?? string.Empty,
                        v => p.Left = v));
                    fields.Add(new TextField(
                        TextFieldKind.MatchRight, "Match (right)", sid, qid,
                        () => p.Right ?? string.Empty,
                        v => p.Right = v));
                }
                AddStringList(fields, sid, qid, TextFieldKind.Distractor,
                    "Distractor", mq.Distractors);
                break;

            case SequenceQuestion sq:
                AddStringList(fields, sid, qid, TextFieldKind.SequenceItem,
                    "Sequence item", sq.Items);
                break;

            case EssayQuestion eq:
                fields.Add(new TextField(
                    TextFieldKind.RubricNotes, "Rubric notes", sid, qid,
                    () => eq.RubricNotes ?? string.Empty,
                    v => eq.RubricNotes = v));
                break;

            // TrueFalseQuestion contributes prompt + hint only: no extra text.
            case TrueFalseQuestion:
                break;

            case DropdownQuestion dd:
                // Same as single-choice: its options are checkable text.
                AddChoices(fields, sid, qid, dd.Choices);
                break;

            // NumericQuestion contributes prompt + hint only; the target is a
            // number and the optional unit is a short label not worth checking.
            case NumericQuestion:
                break;
        }
    }

    private static void AddChoices(List<TextField> fields, Guid sid, Guid qid, List<Choice> choices)
    {
        foreach (var choice in choices)
        {
            var c = choice; // capture per-iteration reference
            fields.Add(new TextField(
                TextFieldKind.ChoiceText, "Choice", sid, qid,
                () => c.Text ?? string.Empty,
                v => c.Text = v));
        }
    }

    /// <summary>
    /// Adds one <see cref="TextField"/> per element of a live
    /// <c>List&lt;string&gt;</c>, whose setter writes back to that exact index.
    /// The index is captured per iteration so each accessor targets its own slot.
    /// </summary>
    private static void AddStringList(
        List<TextField> fields, Guid sid, Guid qid,
        TextFieldKind kind, string label, List<string> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int index = i; // capture
            fields.Add(new TextField(
                kind, label, sid, qid,
                () => list[index] ?? string.Empty,
                v => list[index] = v));
        }
    }
}
