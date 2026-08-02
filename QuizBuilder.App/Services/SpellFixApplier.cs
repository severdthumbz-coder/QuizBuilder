using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;

namespace QuizBuilder.App.Services;

/// <summary>
/// Applies a spelling correction to a <see cref="TextField"/> so it lands in
/// undo and dirty-tracking, not as a raw model poke. Routing (proved in
/// <c>tools/port/spell_fix_apply_port.py</c>) by field kind:
///
/// <list type="bullet">
/// <item>Quiz title / description / section title — through the dedicated
///   service setter (<c>SetTitle</c>/<c>SetDescription</c>/<c>RenameSection</c>),
///   which performs the write and raises the change; no raw set.</item>
/// <item>Study card front/back — raw-set the side via the field, then
///   <c>UpdateStudyCard</c> with the sibling side read back from the live
///   card, so the card's change notification fires.</item>
/// <item>Everything inside a question — raw-set via the field, then
///   <c>NotifyQuestionChanged</c>.</item>
/// </list>
///
/// <para>
/// The undo snapshot is captured BEFORE the mutation, per the UndoService
/// protocol. After an apply the caller must RE-RUN the review: undo restores by
/// replacing the whole <see cref="QuizDocument"/>, which invalidates any
/// <see cref="TextField"/> closures captured against the previous instance —
/// exactly the staleness this project has been bitten by before. Replace here
/// therefore returns void and the panel re-enumerates rather than reusing old
/// results.
/// </para>
/// </summary>
public sealed class SpellFixApplier
{
    private const string UndoLabel = "Spelling correction";

    private readonly IQuizDocumentService _document;
    private readonly IUndoService _undo;

    public SpellFixApplier(IQuizDocumentService document, IUndoService undo)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _undo = undo ?? throw new ArgumentNullException(nameof(undo));
    }

    /// <summary>
    /// Replaces the word at <paramref name="occurrence"/> within its field with
    /// <paramref name="replacement"/>, preserving the surrounding text, and
    /// routes the change through undo + the correct service call. Splicing by
    /// offset (rather than replacing the whole field) means a field containing
    /// the misspelling more than once only fixes the targeted instance.
    /// </summary>
    public void Apply(TextField field, int start, int length, string replacement)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(replacement);

        var current = field.Text;
        if (start < 0 || length < 0 || start + length > current.Length)
        {
            // The field changed under us (a prior fix in the same field shifted
            // offsets, or the document was swapped). Signal the caller to re-run
            // rather than splice at a stale offset.
            throw new InvalidOperationException(
                "Occurrence offset is no longer valid for this field; re-run the review.");
        }

        var spliced = string.Concat(
            current.AsSpan(0, start),
            replacement,
            current.AsSpan(start + length));

        _undo.CaptureBeforeChange(UndoLabel);

        switch (field.Kind)
        {
            case TextFieldKind.QuizTitle:
                _document.SetTitle(spliced);
                break;

            case TextFieldKind.QuizDescription:
                _document.SetDescription(spliced);
                break;

            case TextFieldKind.SectionTitle:
                RequireId(field.SectionId, "section");
                _document.RenameSection(field.SectionId!.Value, spliced);
                break;

            case TextFieldKind.StudyCardFront:
            case TextFieldKind.StudyCardBack:
                RequireId(field.OwnerId, "study card");
                ApplyStudyCard(field, spliced);
                break;

            default:
                // Question-internal: prompt, hint, choices, answers, blanks,
                // match pairs, distractors, sequence items, rubric notes.
                RequireId(field.SectionId, "section");
                RequireId(field.QuestionId, "question");
                field.Set(spliced); // raw-set the model
                _document.NotifyQuestionChanged(
                    field.SectionId!.Value, field.QuestionId!.Value);
                break;
        }
    }

    /// <summary>
    /// Applies the correction to a study card through <c>UpdateStudyCard</c>,
    /// which performs the write and raises StudyCardsChanged. Both sides are
    /// computed from the live card and the target side is swapped for the
    /// corrected text; we must NOT raw-set the field first, because
    /// UpdateStudyCard's no-op guard compares against the current card values —
    /// pre-writing the side would make it see no change and skip the
    /// notification (no dirty flag, no deck rebuild).
    /// </summary>
    private void ApplyStudyCard(TextField field, string correctedSide)
    {
        var cardId = field.OwnerId!.Value;
        var card = _document.Current.StudyCards.FirstOrDefault(c => c.Id == cardId);
        if (card is null)
            throw new InvalidOperationException(
                "The study card being corrected no longer exists; re-run the review.");

        if (field.Kind == TextFieldKind.StudyCardFront)
            _document.UpdateStudyCard(cardId, correctedSide, card.Back);
        else
            _document.UpdateStudyCard(cardId, card.Front, correctedSide);
    }

    private static void RequireId(Guid? id, string what)
    {
        if (id is null)
            throw new InvalidOperationException(
                $"A {what}-owned text field is missing its id; cannot route the correction.");
    }
}
