using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.Core.Services;

/// <summary>
/// The provider-agnostic "brain" of the AI grammar review: builds the prompt
/// from scoped field text, and parses the model's reply back into anchored
/// <see cref="GrammarSuggestion"/>s. No network here — the transport lives in
/// the App-layer providers. This class is pure and unit-tested; the resilient
/// parser (fences, prose-wrapped JSON, object wrappers, malformed output,
/// hallucinated spans) is the risk-bearing part and was proved in
/// <c>tools/port/grammar_prompt_parse_port.py</c> before being written here.
/// </summary>
public static class GrammarReviewEngine
{
    /// <summary>The system instruction: demands JSON-only, no style rewrites,
    /// and a verbatim "original" so each suggestion can be located.</summary>
    public const string SystemInstruction =
        "You are a careful copy-editor for quiz content. You are given numbered " +
        "text fields. Find grammar, spelling, punctuation, and clear phrasing " +
        "problems. Do NOT rewrite for style or tone, do not change meaning, and " +
        "do not flag correct text. Return ONLY a JSON array, no prose, no " +
        "markdown. Each element: {\"field\": <int>, \"original\": \"<exact " +
        "substring from that field>\", \"rewrite\": \"<the corrected substring>\", " +
        "\"reason\": \"<short why>\"}. The \"original\" MUST be copied verbatim " +
        "from the field so it can be located. If there are no problems, return [].";

    /// <summary>True when at least one field has non-whitespace text to review.</summary>
    public static bool HasCheckableText(IReadOnlyList<GrammarField> fields) =>
        fields.Any(f => !string.IsNullOrWhiteSpace(f.Text));

    /// <summary>Builds the user message listing the non-empty fields, numbered.</summary>
    public static string BuildUserPrompt(IReadOnlyList<GrammarField> fields)
    {
        var sb = new StringBuilder();
        sb.Append("Review these fields:\n\n");
        foreach (var f in fields)
        {
            if (!string.IsNullOrWhiteSpace(f.Text))
                sb.Append('[').Append(f.FieldId).Append("] (").Append(f.Label)
                  .Append("): ").Append(f.Text).Append('\n');
        }
        sb.Append("\nReturn the JSON array now.");
        return sb.ToString();
    }

    private static readonly Regex FenceRe =
        new(@"```(?:json)?\s*(.*?)\s*```",
            RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parses a raw model reply into anchored suggestions. Suggestions whose
    /// "original" cannot be located in the referenced field, that reference an
    /// unknown field, or that are no-ops, are dropped rather than surfaced.
    /// </summary>
    public static GrammarReviewResult ParseResponse(string? raw, IReadOnlyList<GrammarField> fields)
    {
        var byId = fields.ToDictionary(f => f.FieldId);

        var jsonText = ExtractJsonText(raw);
        if (jsonText is null)
            return GrammarReviewResult.Failed("Empty response from the model.");

        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(jsonText);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return GrammarReviewResult.Failed("The model's response was not valid JSON.");
        }

        if (!TryCoerceToArray(root, out var items))
            return GrammarReviewResult.Failed("The model's response was not in the expected shape.");

        var suggestions = new List<GrammarSuggestion>();
        foreach (var item in items)
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            if (!TryReadFieldId(item, out var fieldId))
                continue;
            if (!byId.TryGetValue(fieldId, out var field))
                continue;

            var original = ReadString(item, "original");
            var rewrite = ReadString(item, "rewrite");
            var reason = ReadString(item, "reason") ?? ReadString(item, "explanation") ?? string.Empty;

            if (string.IsNullOrEmpty(original) || rewrite is null)
                continue;
            if (original == rewrite)
                continue;

            if (!TryAnchor(field.Text, original, out var start, out var length))
                continue;

            suggestions.Add(new GrammarSuggestion(
                fieldId, start, length,
                field.Text.Substring(start, length), // exact source span
                rewrite, reason));
        }

        return GrammarReviewResult.Ok(suggestions);
    }

    /// <summary>
    /// Pulls the JSON payload out of a reply that may be fenced or prose-wrapped:
    /// a ```json``` fence first, else the outermost bracket span, else the
    /// trimmed whole. Null only when there's nothing at all.
    /// </summary>
    private static string? ExtractJsonText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var fence = FenceRe.Match(raw);
        if (fence.Success)
            return fence.Groups[1].Value.Trim();

        int bracket = IndexOfFirst(raw, '[', '{');
        if (bracket >= 0)
        {
            int end = Math.Max(raw.LastIndexOf(']'), raw.LastIndexOf('}'));
            if (end > bracket)
                return raw.Substring(bracket, end - bracket + 1).Trim();
        }

        return raw.Trim();
    }

    private static int IndexOfFirst(string s, char a, char b)
    {
        int ia = s.IndexOf(a), ib = s.IndexOf(b);
        if (ia == -1) return ib;
        if (ib == -1) return ia;
        return Math.Min(ia, ib);
    }

    /// <summary>Accepts a bare array or a {"suggestions"/"issues"/...: [...]}
    /// wrapper, or a single suggestion object.</summary>
    private static bool TryCoerceToArray(JsonElement root, out List<JsonElement> items)
    {
        items = new List<JsonElement>();

        if (root.ValueKind == JsonValueKind.Array)
        {
            items = root.EnumerateArray().ToList();
            return true;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "suggestions", "issues", "results", "items", "corrections" })
            {
                if (root.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    items = arr.EnumerateArray().ToList();
                    return true;
                }
            }

            // a single unwrapped suggestion object
            if (root.TryGetProperty("field", out _) &&
                root.TryGetProperty("original", out _) &&
                root.TryGetProperty("rewrite", out _))
            {
                items = new List<JsonElement> { root };
                return true;
            }
        }

        return false;
    }

    private static bool TryReadFieldId(JsonElement item, out int fieldId)
    {
        fieldId = 0;
        if (!item.TryGetProperty("field", out var f))
            return false;

        switch (f.ValueKind)
        {
            case JsonValueKind.Number:
                return f.TryGetInt32(out fieldId);
            case JsonValueKind.String:
                return int.TryParse(f.GetString(), out fieldId);
            default:
                return false;
        }
    }

    private static string? ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>
    /// Locates <paramref name="original"/> in <paramref name="fieldText"/>: exact
    /// first, then whitespace-tolerant (models often normalise runs of spaces).
    /// Returns the span into the ORIGINAL text so a splice hits the real offset.
    /// </summary>
    private static bool TryAnchor(string fieldText, string original, out int start, out int length)
    {
        start = 0;
        length = 0;

        if (string.IsNullOrEmpty(original))
            return false;

        int idx = fieldText.IndexOf(original, StringComparison.Ordinal);
        if (idx != -1)
        {
            start = idx;
            length = original.Length;
            return true;
        }

        // Whitespace-tolerant: any run of whitespace in the original matches any
        // run of whitespace in the field.
        var tokens = original.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return false;

        var pattern = string.Join(@"\s+", tokens.Select(Regex.Escape));
        var m = Regex.Match(fieldText, pattern);
        if (m.Success)
        {
            start = m.Index;
            length = m.Length;
            return true;
        }

        return false;
    }
}
