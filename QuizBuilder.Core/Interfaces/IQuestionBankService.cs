using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// One question kept in the reusable bank. The bank is a pool an author draws
/// from across quizzes: a stored question is copied into a quiz, never moved, so
/// the same question can seed many quizzes and editing it in one quiz leaves the
/// bank copy untouched.
///
/// Bank questions are text only. Images live inside a quiz's .qbx package,
/// content-addressed; the bank is a separate file with no package around it, so
/// a question pulled from the bank arrives without an image and the author adds
/// one in the quiz if they want it. Keeping the bank image-free avoids a second,
/// parallel image store for a feature few bank questions would use.
/// </summary>
public sealed class BankEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The question, stored with the same $kind discriminator as a .qbx.</summary>
    public Question Question { get; set; } = default!;

    /// <summary>
    /// An optional free-text grouping -- a topic, unit, or difficulty -- used to
    /// filter the bank. One loose string rather than a fixed taxonomy: authors
    /// organise differently, and a single tag covers the common case without
    /// imposing a structure.
    /// </summary>
    public string? Category { get; set; }

    public DateTimeOffset AddedUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Stores the reusable question bank in question-bank.json beside the exe,
/// mirroring the other local stores (history, paused attempts, settings).
/// </summary>
public interface IQuestionBankService
{
    /// <summary>Every stored question, newest first.</summary>
    IReadOnlyList<BankEntry> All();

    /// <summary>The distinct categories in use, for filtering. Excludes blanks.</summary>
    IReadOnlyList<string> Categories();

    /// <summary>
    /// Stores a copy of a question in the bank and returns the new entry. The
    /// question is cloned on the way in, so later edits to the caller's copy do
    /// not reach the bank.
    /// </summary>
    BankEntry Add(Question question, string? category);

    /// <summary>Updates an entry's category (the only field an author edits in place).</summary>
    void SetCategory(Guid entryId, string? category);

    void Remove(Guid entryId);

    void Load();

    event EventHandler? BankChanged;
}
