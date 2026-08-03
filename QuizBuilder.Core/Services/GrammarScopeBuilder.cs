using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.Core.Services;

/// <summary>The part of a quiz an AI grammar check covers.</summary>
public enum GrammarScope
{
    /// <summary>One section's fields (its title, and its questions' text).</summary>
    Section,

    /// <summary>The study cards only.</summary>
    StudyCards,

    /// <summary>Every authored field in the quiz.</summary>
    WholeQuiz,
}

/// <summary>The engine input plus the mapping needed to route an accepted
/// suggestion back to the real field it came from.</summary>
public sealed class GrammarScopeSelection
{
    public GrammarScopeSelection(
        IReadOnlyList<GrammarField> fields,
        IReadOnlyDictionary<int, TextField> backMap,
        IReadOnlyDictionary<int, bool> replaceable)
    {
        Fields = fields;
        BackMap = backMap;
        Replaceable = replaceable;
    }

    /// <summary>What the engine reviews (HTML-stripped, non-empty, id-assigned).</summary>
    public IReadOnlyList<GrammarField> Fields { get; }

    /// <summary>Assigned field id → the source <see cref="TextField"/>, so an
    /// accepted rewrite can be applied via SpellFixApplier.</summary>
    public IReadOnlyDictionary<int, TextField> BackMap { get; }

    /// <summary>Assigned field id → whether an accept can splice in place. False
    /// for the description (offsets are on stripped text), mirroring spelling.</summary>
    public IReadOnlyDictionary<int, bool> Replaceable { get; }

    public bool HasFields => Fields.Count > 0;
}

/// <summary>
/// Bridges the document text inventory to the grammar engine: filters to the
/// chosen <see cref="GrammarScope"/>, strips the description's markup (reusing
/// <see cref="DescriptionParser.ToPlainText"/> so tag names never reach the
/// model), drops empty fields, assigns stable ids, and keeps a back-map from
/// each id to its source field. Proved in
/// <c>tools/port/grammar_scope_port.py</c>; pinned by <c>GrammarScopeBuilderTests</c>.
/// </summary>
public static class GrammarScopeBuilder
{
    private static readonly HashSet<TextFieldKind> StudyCardKinds = new()
    {
        TextFieldKind.StudyCardFront, TextFieldKind.StudyCardBack,
    };

    /// <summary>
    /// Builds the scoped selection. <paramref name="sectionId"/> is required for
    /// <see cref="GrammarScope.Section"/> and ignored otherwise.
    /// </summary>
    public static GrammarScopeSelection Build(
        IReadOnlyList<TextField> inventory,
        GrammarScope scope,
        Guid? sectionId = null)
    {
        var selected = Select(inventory, scope, sectionId);

        var fields = new List<GrammarField>();
        var backMap = new Dictionary<int, TextField>();
        var replaceable = new Dictionary<int, bool>();

        int nextId = 0;
        foreach (var tf in selected)
        {
            var isDescription = tf.Kind == TextFieldKind.QuizDescription;
            var text = isDescription
                ? DescriptionParser.ToPlainText(tf.Text)
                : tf.Text;

            if (string.IsNullOrWhiteSpace(text))
                continue;

            fields.Add(new GrammarField(nextId, tf.Label, text));
            backMap[nextId] = tf;
            replaceable[nextId] = !isDescription;
            nextId++;
        }

        return new GrammarScopeSelection(fields, backMap, replaceable);
    }

    private static IEnumerable<TextField> Select(
        IReadOnlyList<TextField> inventory, GrammarScope scope, Guid? sectionId)
    {
        switch (scope)
        {
            case GrammarScope.WholeQuiz:
                return inventory;

            case GrammarScope.StudyCards:
                return inventory.Where(f => StudyCardKinds.Contains(f.Kind));

            case GrammarScope.Section:
                return sectionId is null
                    ? Enumerable.Empty<TextField>()
                    : inventory.Where(f => f.SectionId == sectionId);

            default:
                return Enumerable.Empty<TextField>();
        }
    }
}
