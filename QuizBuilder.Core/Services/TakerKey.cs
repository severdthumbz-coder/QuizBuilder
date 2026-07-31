namespace QuizBuilder.Core.Services;

/// <summary>
/// One place that turns a taker's email into the normalized key used to scope
/// history and paused sittings per person on a shared device. Every call site
/// (the record builder, the storage services, the player) must key identically,
/// or the same person would fail to match their own records -- so the rule lives
/// here and nowhere else.
///
/// <para>
/// The rule: trim surrounding whitespace and lower-case invariantly, so
/// "Bob@X.com", "bob@x.com", and " bob@x.com " are one session. Email is the
/// key (not name): a taker who mistypes their name on a later sitting still sees
/// their history. An empty or whitespace email yields null, meaning "no
/// identity" -- such records are treated as legacy and shown to everyone.
/// </para>
/// </summary>
public static class TakerKey
{
    /// <summary>Normalized key for an email, or null when there is no usable email.</summary>
    public static string? Normalize(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    /// <summary>
    /// True when a stored record's key belongs to the signed-in taker. A record
    /// with a null key is legacy (written before identity scoping) and matches
    /// everyone, so nothing disappears from view. A record with a key matches
    /// only that exact normalized key.
    /// </summary>
    public static bool Matches(string? recordKey, string? takerKey) =>
        recordKey is null || recordKey == takerKey;
}
