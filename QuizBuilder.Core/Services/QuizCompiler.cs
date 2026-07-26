using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Models;

namespace QuizBuilder.Core.Services;

/// <summary>
/// One question as it will actually appear on a paper: after selection,
/// shuffling, and numbering.
/// </summary>
public sealed class CompiledQuestion
{
    public required Question Question { get; init; }

    /// <summary>1-based number as printed, continuous across sections.</summary>
    public required int Number { get; init; }

    /// <summary>
    /// The right-hand column for a matching question, shuffled and including
    /// distractors. Null for every other type. Pre-computed here rather than
    /// in the view, so Preview and an exported PDF cannot disagree about which
    /// order the student saw.
    /// </summary>
    public IReadOnlyList<string>? MatchingOptions { get; init; }

    /// <summary>
    /// The order sequence items are first shown to the taker, as a permutation
    /// of the authored indices 0..n-1. Null for every other type. Like
    /// <see cref="MatchingOptions"/>, this is a presentation projection: the
    /// model's own <c>Items</c> stay in correct order (they are the answer key
    /// the grader compares against), and the shuffle lives only here.
    /// <para>
    /// Never the identity permutation for n>=2 when randomising: showing items
    /// already in order would hand the taker the answer. When the author has
    /// turned randomisation off, this is the identity order, matching how a
    /// fixed-order matching question presents.
    /// </para>
    /// </summary>
    public IReadOnlyList<int>? SequencePresentation { get; init; }
}

public sealed class CompiledSection
{
    public required Guid SourceSectionId { get; init; }
    public required string Title { get; init; }
    public required IReadOnlyList<CompiledQuestion> Questions { get; init; }

    public double TotalPoints => Questions.Sum(q => q.Question.Points);
}

public sealed class CompiledQuiz
{
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<CompiledSection> Sections { get; init; }
    public int? TimeLimitMinutes { get; init; }

    /// <summary>The seed that produced this paper. Reusing it reproduces it exactly.</summary>
    public required int Seed { get; init; }

    /// <summary>
    /// Config problems worth surfacing, e.g. asking for more questions than a
    /// section holds. Advisory: the paper is still produced.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    public int QuestionCount => Sections.Sum(s => s.Questions.Count);
    public double TotalPoints => Sections.Sum(s => s.TotalPoints);

    /// <summary>Percentage needed to pass -- of questions or points, per Basis.</summary>
    public required int PassPercentage { get; init; }

    /// <summary>Whether the pass mark counts questions or points.</summary>
    public required PassMarkBasis PassMarkBasis { get; init; }

    /// <summary>
    /// Questions that can actually be got right or wrong. A question worth 0
    /// points is excluded: counting it as incorrect would put 100% out of
    /// reach through no fault of the student, and it clearly is not meant to
    /// carry the grade.
    /// </summary>
    public int GradeableQuestionCount =>
        Sections.Sum(s => s.Questions.Count(q => q.Question.Points > 0));

    /// <summary>
    /// Questions that must be correct to pass, under QuestionCount. Rounded UP:
    /// 75% of 5 questions is 3.75, and 3 of 5 is 60%, not 75%. Rounding down
    /// would quietly lower the bar.
    /// </summary>
    public int QuestionsToPass =>
        GradeableQuestionCount <= 0
            ? 0
            : (int)Math.Ceiling(GradeableQuestionCount * PassPercentage / 100d);

    /// <summary>
    /// Points needed to pass, for DISPLAY, under TotalPoints. Rounded up to two
    /// decimals so the printed bar is never lower than the real one: a student
    /// told they need 2.47 who then fails on 2.475 has a legitimate complaint,
    /// whereas being told 2.48 and passing on 2.475 is a pleasant surprise.
    ///
    /// This is deliberately NOT the rule <see cref="PassesOnPoints"/> applies --
    /// that compares exact percentages with no rounding.
    /// </summary>
    public double PointsToPass =>
        TotalPoints <= 0 ? 0 : Math.Ceiling(TotalPoints * PassPercentage) / 100d;

    /// <summary>
    /// Whether a single question counts as correct: at least half its own
    /// points. Only matters for partially-credited types -- multiple choice
    /// and true/false score all-or-nothing regardless.
    ///
    /// Null for a 0-point question, which is not gradeable either way.
    /// </summary>
    public static bool? QuestionIsCorrect(double scored, double possible) =>
        possible <= 0 ? null : scored / possible >= QuizSettings.CorrectAtFraction;

    /// <summary>
    /// Whether a set of per-question scores passes. The scores are keyed by
    /// the CompiledQuestion, since a compiled question's Id is freshly minted
    /// by Clone() and cannot be matched back to the authored question.
    ///
    /// Null when there is nothing to grade: neither a pass nor a fail, and the
    /// alternative is a divide by zero.
    /// </summary>
    public bool? Passes(IReadOnlyDictionary<CompiledQuestion, double> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);

        return PassMarkBasis switch
        {
            PassMarkBasis.TotalPoints => PassesOnPoints(scores.Values.Sum()),
            PassMarkBasis.QuestionCount => PassesOnQuestions(scores),
            _ => null,
        };
    }

    /// <summary>
    /// The points rule: exact percentage of the total, no rounding.
    /// <see cref="PointsToPass"/> is a rounded display value and may sit a
    /// hundredth above this bar.
    /// </summary>
    public bool? PassesOnPoints(double scoredPoints) =>
        TotalPoints <= 0 ? null : scoredPoints / TotalPoints * 100 >= PassPercentage;

    /// <summary>
    /// The question rule: how many questions were correct, out of the gradeable
    /// ones. A question missing from the scores counts as zero -- unanswered.
    /// </summary>
    public bool? PassesOnQuestions(IReadOnlyDictionary<CompiledQuestion, double> scores)
    {
        var gradeable = Sections
            .SelectMany(s => s.Questions)
            .Where(q => q.Question.Points > 0)
            .ToList();

        if (gradeable.Count == 0) return null;

        var correct = gradeable.Count(q =>
            QuestionIsCorrect(scores.TryGetValue(q, out var scored) ? scored : 0,
                              q.Question.Points) == true);

        return (double)correct / gradeable.Count * 100 >= PassPercentage;
    }

}

/// <summary>
/// Turns an authored quiz plus its settings into the paper a student would see.
///
/// This lives in Core, not the WPF layer, for two reasons: it is pure logic and
/// therefore testable without a UI, and Publish will need the identical output
/// later. If Preview computed its own shuffle, an exported PDF could differ
/// from what was previewed -- which is the kind of bug nobody finds until the
/// papers are printed.
///
/// Everything is derived from a seed. Without one, every repaint would reshuffle
/// and "Preview" would never show the same paper twice.
/// </summary>
public sealed class QuizCompiler : IQuizCompiler
{
    public CompiledQuiz Compile(QuizDocument document, QuizSettings settings, int seed, IReadOnlySet<Guid>? includedSectionIds = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(settings);

        var rng = new Random(seed);
        var warnings = new List<string>();
        var sections = new List<CompiledSection>();

        var number = 1;

        // Published order, not authoring order: SectionDisplayOrder is what a
        // reader sees, and it may legitimately differ.
        //
        // When the taker chose sections at quiz time, honour that choice here.
        // The filter applies ONLY under SelectAtQuizTime: a stale selection set
        // passed under any other scope is ignored, so it can never silently drop
        // sections from a normally-graded quiz.
        var runtimeFilter = settings.GradingScope == GradingScope.SelectAtQuizTime
            ? includedSectionIds
            : null;

        var includedSections = document.SectionsInDisplayOrder()
            .Where(s => runtimeFilter is null || runtimeFilter.Contains(s.Id))
            .ToList();

        // TotalCount distributes one quiz-wide number across the included
        // sections in proportion to their pools. Computed here, once, because it
        // needs to see every section at the same time -- unlike the other modes,
        // which decide each section independently. The result maps section id to
        // how many questions that section should contribute.
        var totalCountTargets = settings.SelectionMode == QuestionSelectionMode.TotalCount
            ? DistributeProportionally(includedSections, settings.TotalQuestionCount)
            : null;

        foreach (var section in includedSections)
        {
            var selected = SelectQuestions(section, settings, rng, warnings, totalCountTargets);

            if (settings.RandomizeQuestionOrder)
                Shuffle(selected, rng);

            var compiled = new List<CompiledQuestion>();
            foreach (var question in selected)
            {
                // Clone before touching anything: shuffling choices must not
                // reorder the author's own document. Clone() mints a new Id, so
                // nothing downstream may match these back to authored ids.
                var copy = question.Clone();

                if (settings.RandomizeAnswerOrder)
                    ShuffleAnswers(copy, rng);

                compiled.Add(new CompiledQuestion
                {
                    Question = copy,
                    Number = number++,
                    MatchingOptions = BuildMatchingOptions(copy, settings, rng),
                    SequencePresentation = BuildSequencePresentation(copy, settings, rng),
                });
            }

            sections.Add(new CompiledSection
            {
                SourceSectionId = section.Id,
                Title = section.Title,
                Questions = compiled,
            });
        }

        if (sections.Count == 0)
            warnings.Add("This quiz has no sections yet.");
        else if (sections.All(s => s.Questions.Count == 0))
            warnings.Add("No questions would appear on this paper.");

        if (settings.PassPercentage == 0)
        {
            warnings.Add("The pass mark is 0%, so every paper passes. "
                         + "Set it in Settings under Grading scope.");
        }

        return new CompiledQuiz
        {
            Title = document.Title,
            Description = document.Description,
            Sections = sections,
            TimeLimitMinutes = settings.TimeLimitMinutes,
            PassPercentage = settings.PassPercentage,
            PassMarkBasis = settings.PassMarkBasis,
            Seed = seed,
            Warnings = warnings,
        };
    }

    private static List<Question> SelectQuestions(
        Section section, QuizSettings settings, Random rng, List<string> warnings,
        IReadOnlyDictionary<Guid, int>? totalCountTargets = null)
    {
        var all = section.Questions.ToList();

        // TotalCount: the quiz-wide distribution already decided this section's
        // share. Take that many at random. No per-section warnings here -- the
        // distribution never asks for more than the section holds.
        if (settings.SelectionMode == QuestionSelectionMode.TotalCount)
        {
            if (totalCountTargets is null || !totalCountTargets.TryGetValue(section.Id, out var share))
                return all;

            if (share >= all.Count) return all;
            if (share <= 0) return new List<Question>();

            return all.OrderBy(_ => rng.Next()).Take(share).ToList();
        }

        if (settings.SelectionMode != QuestionSelectionMode.ExactCountPerSection)
            return all;

        // A section with no configured count takes ALL its questions. Defaulting
        // to zero would silently delete it from the paper, and a section the
        // author never configured is far more likely to be an oversight than a
        // deliberate omission.
        if (!settings.QuestionCountPerSection.TryGetValue(section.Id.ToString(), out var want))
            return all;

        if (want >= all.Count)
        {
            if (want > all.Count)
            {
                warnings.Add(
                    $"\"{section.Title}\" is set to {want} questions but only has {all.Count}. "
                    + "Every question will be used.");
            }

            return all;
        }

        if (want <= 0)
        {
            // Kept as an empty section rather than dropped: a section that
            // vanishes from the paper looks like a bug, whereas an empty one
            // shows the setting is doing exactly what it was told.
            warnings.Add($"\"{section.Title}\" is set to 0 questions, so it will be empty.");
            return new List<Question>();
        }

        // Take a RANDOM subset, not the first N: otherwise "pick 5 of 20" hands
        // every student the same five questions.
        return all.OrderBy(_ => rng.Next()).Take(want).ToList();
    }

    /// <summary>
    /// Spreads a single total across sections in proportion to each section's
    /// pool, using largest-remainder (Hamilton) apportionment. Returns a map of
    /// section id to how many questions that section should contribute.
    ///
    /// A section's proportional share is total x pool / grandTotal. Because the
    /// total is first clamped to the grand total, that share can never exceed a
    /// section's own pool (that would need total > grandTotal), so no section is
    /// ever asked for more than it has -- the distribution is naturally
    /// self-capping and the shares always sum to exactly the requested total.
    /// </summary>
    private static Dictionary<Guid, int> DistributeProportionally(IReadOnlyList<Section> sections, int total)
    {
        var result = new Dictionary<Guid, int>();

        var pools = sections.Select(s => s.Questions.Count).ToList();
        var grandTotal = pools.Sum();

        if (grandTotal == 0)
        {
            foreach (var section in sections) result[section.Id] = 0;
            return result;
        }

        // Clamp: asking for more than the whole quiz simply takes the whole quiz.
        var target = Math.Clamp(total, 0, grandTotal);

        // Ideal (fractional) share and its floor for each section.
        var ideal = pools.Select(p => (double)target * p / grandTotal).ToList();
        var take = ideal.Select(x => (int)Math.Floor(x)).ToList();

        // Hand out the rounding leftover to the largest fractional remainders,
        // skipping any section already at its pool (belt-and-braces: the maths
        // above guarantees room, but the guard makes the invariant explicit).
        var leftover = target - take.Sum();

        var order = Enumerable.Range(0, pools.Count)
            .OrderByDescending(i => ideal[i] - take[i])
            .ThenByDescending(i => pools[i])
            .ThenBy(i => i)
            .ToList();

        var pos = 0;
        while (leftover > 0 && pos < order.Count * 4)
        {
            var i = order[pos % order.Count];
            if (take[i] < pools[i])
            {
                take[i]++;
                leftover--;
            }

            pos++;
        }

        for (var i = 0; i < sections.Count; i++)
            result[sections[i].Id] = take[i];

        return result;
    }

    private static void Shuffle<T>(IList<T> items, Random rng)
    {
        // Fisher-Yates. OrderBy(_ => rng.Next()) is subtly biased and evaluates
        // the key lazily, which has burned people via repeated enumeration.
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>
    /// Shuffles a question's options where that is meaningful. True/False is
    /// left alone: its order is conventional, and a paper reading "False /
    /// True" looks like a rendering fault. Fill-in-the-blank blanks are
    /// positional, tied to {{n}} in the prompt, so reordering them would break
    /// the question.
    /// </summary>
    private static void ShuffleAnswers(Question question, Random rng)
    {
        switch (question)
        {
            case MultipleChoiceSingleQuestion q:
                Shuffle(q.Choices, rng);
                break;

            case MultipleChoiceMultipleQuestion q:
                Shuffle(q.Choices, rng);
                break;

            // Matching shuffles via MatchingOptions instead: the left column
            // must keep its order so the printed rows stay stable.
            case MatchingQuestion:

            // Sequence must NOT shuffle here. Items is the answer key, stored
            // in the correct order; shuffling it in place would destroy the
            // very thing the grader compares against. Presentation order is a
            // separate concern, carried on the compiled question rather than
            // by mutating the model.
            case SequenceQuestion:

            case TrueFalseQuestion:
            case ShortAnswerQuestion:
            case FillInTheBlankQuestion:
            case EssayQuestion:
            default:
                break;
        }
    }

    /// <summary>
    /// The right-hand column a student picks from: every pair's right value plus
    /// any distractors. Shuffled unless the author asked for a fixed order --
    /// unshuffled, the answer is just "match row 1 to row 1".
    /// </summary>
    private static IReadOnlyList<string>? BuildMatchingOptions(
        Question question, QuizSettings settings, Random rng)
    {
        if (question is not MatchingQuestion matching) return null;

        var options = matching.Pairs
            .Select(p => p.Right)
            .Concat(matching.Distractors)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        if (settings.RandomizeAnswerOrder)
            Shuffle(options, rng);

        return options;
    }

    /// <summary>
    /// The order a sequence question's items are first shown to the taker: a
    /// permutation of 0..n-1 into the authored (correct) order. The model's
    /// Items are left untouched -- they are the answer key.
    /// <para>
    /// With randomisation on we shuffle, but must never present the identity
    /// for n>=2 (that would show the items already in order). Fisher-Yates can
    /// legitimately land on identity, so a rotate-by-one fallback guarantees a
    /// non-identity arrangement without introducing bias into the common case.
    /// With randomisation off we return identity, matching a fixed-order
    /// matching question: the author has chosen to show the correct order.
    /// </para>
    /// </summary>
    private static IReadOnlyList<int>? BuildSequencePresentation(
        Question question, QuizSettings settings, Random rng)
    {
        if (question is not SequenceQuestion sequence) return null;

        var n = sequence.Items.Count;
        var order = Enumerable.Range(0, n).ToList();

        // Identity is the right presentation when there is nothing to arrange
        // (n < 2) or the author asked for a fixed order.
        if (n < 2 || !settings.RandomizeAnswerOrder)
            return order;

        Shuffle(order, rng);

        // Guard: an honest shuffle can still produce the correct order. Rotating
        // the identity left by one is guaranteed non-identity for n >= 2 and is
        // only reached in the rare case the shuffle collided with identity.
        if (IsIdentity(order))
        {
            order = Enumerable.Range(0, n).ToList();
            var first = order[0];
            order.RemoveAt(0);
            order.Add(first);
        }

        return order;
    }

    private static bool IsIdentity(IReadOnlyList<int> order)
    {
        for (var i = 0; i < order.Count; i++)
            if (order[i] != i) return false;
        return true;
    }
}
