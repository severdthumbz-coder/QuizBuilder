using System.Text.Json;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;

namespace QuizBuilder.App.Services;

/// <summary>
/// The user's custom dictionary ("Add to dictionary" from the review panel):
/// words the spell-checker should treat as correct. Persisted through
/// <see cref="ISettingsService"/> in <c>AppSettings.Extra</c> under a namespaced
/// key, so it rides the existing portable settings.json beside the exe and
/// needs no schema change and no .qbx change (an ignore-list is desktop-local,
/// exactly like the APK link).
///
/// <para>
/// Stored as a JSON array of the ORIGINAL surface forms (so the settings file
/// stays human-readable), but compared normalized — trimmed + lower-invariant,
/// via <see cref="SpellReviewEngine.NormalizeIgnoreWord"/> — so adding
/// "Photosynthesis" also ignores "photosynthesis". De-duplicated on the
/// normalized form.
/// </para>
/// </summary>
public sealed class SpellIgnoreListStore
{
    // Namespaced per the AppSettings.Extra convention ("keys should be
    // namespaced, e.g. preview.zoomLevel").
    private const string ExtraKey = "spellcheck.ignoreWords";

    private readonly ISettingsService _settings;

    public SpellIgnoreListStore(ISettingsService settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>The current ignore-list (original surface forms), for the engine
    /// and for display in settings.</summary>
    public IReadOnlyList<string> GetWords()
    {
        if (!_settings.Current.Extra.TryGetValue(ExtraKey, out var json)
            || string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<string>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (JsonException)
        {
            // A hand-corrupted value must not break spell-check; treat as empty.
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Adds a word if its normalized form isn't already present. Returns true
    /// if the list changed (and was saved). No-ops on blank input.
    /// </summary>
    public bool Add(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        var normalized = SpellReviewEngine.NormalizeIgnoreWord(word);
        var current = GetWords().ToList();

        if (current.Any(w => SpellReviewEngine.NormalizeIgnoreWord(w) == normalized))
            return false;

        current.Add(word.Trim());
        Persist(current);
        return true;
    }

    /// <summary>Removes a word by normalized match. Returns true if removed.</summary>
    public bool Remove(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return false;

        var normalized = SpellReviewEngine.NormalizeIgnoreWord(word);
        var current = GetWords().ToList();

        int removed = current.RemoveAll(
            w => SpellReviewEngine.NormalizeIgnoreWord(w) == normalized);
        if (removed == 0)
            return false;

        Persist(current);
        return true;
    }

    private void Persist(List<string> words)
    {
        _settings.Current.Extra[ExtraKey] = JsonSerializer.Serialize(words);
        _settings.Save();
    }
}
