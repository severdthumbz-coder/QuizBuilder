using System.Text.Json.Serialization;

namespace QuizBuilder.Core.Models;

public enum QuestionKind
{
    MultipleChoiceSingle,
    MultipleChoiceMultiple,
    TrueFalse,
    ShortAnswer,
    FillInTheBlank,
    Matching,
    Essay,

    // Appended last: these are persisted by name in .qbx but by value in some
    // spreadsheet round-trips, so inserting earlier would renumber the rest.
    Sequence
}

/// <summary>
/// Base for every question type.
///
/// Serialization note: the [JsonDerivedType] discriminators below are part of
/// the .qbx file format. Renaming a discriminator string breaks every file a
/// user has already saved -- treat them as a published contract. Adding a new
/// derived type is safe; removing one is not.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(MultipleChoiceSingleQuestion), "mc-single")]
[JsonDerivedType(typeof(MultipleChoiceMultipleQuestion), "mc-multi")]
[JsonDerivedType(typeof(TrueFalseQuestion), "true-false")]
[JsonDerivedType(typeof(ShortAnswerQuestion), "short-answer")]
[JsonDerivedType(typeof(FillInTheBlankQuestion), "fill-blank")]
[JsonDerivedType(typeof(MatchingQuestion), "matching")]
[JsonDerivedType(typeof(EssayQuestion), "essay")]
[JsonDerivedType(typeof(SequenceQuestion), "sequence")]
public abstract class Question
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Prompt { get; set; } = string.Empty;

    public double Points { get; set; } = 1;

    /// <summary>Optional hint shown to the student.</summary>
    public string? Hint { get; set; }

    /// <summary>
    /// Path to an attached image, relative to the .qbx package root
    /// (e.g. "images/3f2a....png"). Null when no image is attached.
    /// Stored as a package-relative path -- never an absolute disk path,
    /// which would not survive being opened on another machine.
    /// </summary>
    public string? ImageRelativePath { get; set; }

    [JsonIgnore]
    public abstract QuestionKind Kind { get; }

    /// <summary>Short human-readable label for the question type.</summary>
    [JsonIgnore]
    public abstract string KindDisplayName { get; }

    public abstract Question Clone();

    protected void CopyBaseTo(Question target)
    {
        target.Id = Guid.NewGuid();
        target.Prompt = Prompt;
        target.Points = Points;
        target.Hint = Hint;
        target.ImageRelativePath = ImageRelativePath;
    }
}

/// <summary>A selectable answer option.</summary>
public sealed class Choice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }

    public Choice Clone() => new() { Id = Guid.NewGuid(), Text = Text, IsCorrect = IsCorrect };
}

public sealed class MultipleChoiceSingleQuestion : Question
{
    public override QuestionKind Kind => QuestionKind.MultipleChoiceSingle;
    public override string KindDisplayName => "Multiple Choice";

    public List<Choice> Choices { get; set; } = new();

    public override Question Clone()
    {
        var c = new MultipleChoiceSingleQuestion
        {
            Choices = Choices.Select(x => x.Clone()).ToList()
        };
        CopyBaseTo(c);
        return c;
    }
}

public sealed class MultipleChoiceMultipleQuestion : Question
{
    public override QuestionKind Kind => QuestionKind.MultipleChoiceMultiple;
    public override string KindDisplayName => "Multiple Choice (multiple answers)";

    public List<Choice> Choices { get; set; } = new();

    /// <summary>
    /// When true, a partly-right answer earns part of the marks.
    ///
    /// The rule is (correct picks - incorrect picks) / total correct, floored at
    /// zero -- NOT simply "credit per correct selection", which would award full
    /// marks for ticking every box and make the question free. Wrong picks have
    /// to cost something or there is no reason not to select everything.
    /// </summary>
    public bool AllowPartialCredit { get; set; } = true;

    public override Question Clone()
    {
        var c = new MultipleChoiceMultipleQuestion
        {
            Choices = Choices.Select(x => x.Clone()).ToList(),
            AllowPartialCredit = AllowPartialCredit
        };
        CopyBaseTo(c);
        return c;
    }
}

public sealed class TrueFalseQuestion : Question
{
    public override QuestionKind Kind => QuestionKind.TrueFalse;
    public override string KindDisplayName => "True / False";

    public bool CorrectAnswer { get; set; } = true;

    public override Question Clone()
    {
        var c = new TrueFalseQuestion { CorrectAnswer = CorrectAnswer };
        CopyBaseTo(c);
        return c;
    }
}

public sealed class ShortAnswerQuestion : Question
{
    public override QuestionKind Kind => QuestionKind.ShortAnswer;
    public override string KindDisplayName => "Short Answer";

    /// <summary>
    /// Any one of these counts as correct. Multiple entries allow for
    /// spelling variants ("colour" / "color").
    /// </summary>
    public List<string> AcceptedAnswers { get; set; } = new();

    public bool CaseSensitive { get; set; }

    public override Question Clone()
    {
        var c = new ShortAnswerQuestion
        {
            AcceptedAnswers = new List<string>(AcceptedAnswers),
            CaseSensitive = CaseSensitive
        };
        CopyBaseTo(c);
        return c;
    }
}

/// <summary>
/// A blank is represented in <see cref="Question.Prompt"/> by the token
/// {{1}}, {{2}}, ... matching <see cref="Blank.Ordinal"/>.
/// </summary>
public sealed class FillInTheBlankQuestion : Question
{
    public override QuestionKind Kind => QuestionKind.FillInTheBlank;
    public override string KindDisplayName => "Fill in the Blank";

    public List<Blank> Blanks { get; set; } = new();
    public bool CaseSensitive { get; set; }

    public override Question Clone()
    {
        var c = new FillInTheBlankQuestion
        {
            Blanks = Blanks.Select(b => b.Clone()).ToList(),
            CaseSensitive = CaseSensitive
        };
        CopyBaseTo(c);
        return c;
    }
}

public sealed class Blank
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>1-based position, matching the {{n}} token in the prompt.</summary>
    public int Ordinal { get; set; } = 1;

    public List<string> AcceptedAnswers { get; set; } = new();

    public Blank Clone() => new()
    {
        Id = Guid.NewGuid(),
        Ordinal = Ordinal,
        AcceptedAnswers = new List<string>(AcceptedAnswers)
    };
}

public sealed class MatchingQuestion : Question
{
    public override QuestionKind Kind => QuestionKind.Matching;
    public override string KindDisplayName => "Matching";

    public List<MatchPair> Pairs { get; set; } = new();

    /// <summary>
    /// Extra right-hand options with no left-hand match, to defeat
    /// answering by elimination.
    /// </summary>
    public List<string> Distractors { get; set; } = new();

    public override Question Clone()
    {
        var c = new MatchingQuestion
        {
            Pairs = Pairs.Select(p => p.Clone()).ToList(),
            Distractors = new List<string>(Distractors)
        };
        CopyBaseTo(c);
        return c;
    }
}

public sealed class MatchPair
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Left { get; set; } = string.Empty;
    public string Right { get; set; } = string.Empty;

    public MatchPair Clone() => new() { Id = Guid.NewGuid(), Left = Left, Right = Right };
}

/// <summary>
/// The taker drags items into the correct order.
/// <para>
/// <see cref="Items"/> is stored in the <i>correct</i> order: authoring a
/// sequence means writing the steps down in order, and asking the author to
/// also specify a separate presentation order would be busywork. The compiler
/// shuffles for presentation, guarding against shuffling back into the correct
/// order, which would make the question a free point.
/// </para>
/// </summary>
public sealed class SequenceQuestion : Question
{
    public override QuestionKind Kind => QuestionKind.Sequence;
    public override string KindDisplayName => "Sequence";

    /// <summary>The items in their correct order.</summary>
    public List<string> Items { get; set; } = new();

    public override Question Clone()
    {
        var c = new SequenceQuestion
        {
            Items = new List<string>(Items)
        };
        CopyBaseTo(c);
        return c;
    }
}

public sealed class EssayQuestion : Question
{
    public override QuestionKind Kind => QuestionKind.Essay;
    public override string KindDisplayName => "Essay";

    /// <summary>Guidance shown to whoever grades the response.</summary>
    public string? RubricNotes { get; set; }

    /// <summary>Zero means no suggested limit.</summary>
    public int SuggestedWordCount { get; set; }

    public override Question Clone()
    {
        var c = new EssayQuestion
        {
            RubricNotes = RubricNotes,
            SuggestedWordCount = SuggestedWordCount
        };
        CopyBaseTo(c);
        return c;
    }
}

public sealed class Section
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Untitled Section";
    public List<Question> Questions { get; set; } = new();

    public Section Clone() => new()
    {
        Id = Guid.NewGuid(),
        Title = Title,
        Questions = Questions.Select(q => q.Clone()).ToList()
    };
}

/// <summary>
/// The quiz itself. Held by IQuizDocumentService as a single shared instance
/// that the Builder, Settings, Theme, Preview and Publish tabs all read.
/// </summary>
/// <summary>
/// One hand-authored study card: a front and a back, both plain text.
///
/// Deliberately not a Question. A study card is not graded, has no type, no
/// points, no correct-answer machinery -- conflating it with Question would drag
/// all of that in for no reason. Images are a later addition; this stays text
/// until then.
/// </summary>
public sealed class StudyCard
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The prompt side -- a term, a question, a concept.</summary>
    public string Front { get; set; } = string.Empty;

    /// <summary>The reveal side -- the definition, answer, or explanation.</summary>
    public string Back { get; set; } = string.Empty;

    /// <summary>
    /// Optional image on the front, package-relative (e.g. "images/ab12...png").
    /// A card is two-sided, so each side carries its own image independently --
    /// "identify this" on the front, a labelled answer on the back.
    /// </summary>
    public string? FrontImageRelativePath { get; set; }

    /// <summary>Optional image on the back, package-relative.</summary>
    public string? BackImageRelativePath { get; set; }
}

public sealed class QuizDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Untitled Quiz";

    /// <summary>
    /// Optional blurb shown under the title: instructions, context, exam
    /// conditions. Empty rather than null when unset, so callers do not each
    /// need a null check. Unlike a section name, a blank description is a
    /// legitimate choice and is never coerced to a placeholder.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    public List<Section> Sections { get; set; } = new();

    /// <summary>
    /// Hand-authored study cards: front/back pairs for the Flash Cards tab,
    /// independent of the quiz questions. A learner often wants to drill
    /// "term -> definition" material that is not itself a graded question.
    ///
    /// Stored on the document so they travel in the .qbx exactly like the
    /// questions do -- a plain list, no special serialisation.
    /// </summary>
    public List<StudyCard> StudyCards { get; set; } = new();

    /// <summary>Id of the active theme (built-in id, or the custom theme).</summary>
    public string ThemeId { get; set; } = Theming.BuiltInThemes.AcademicId;

    /// <summary>
    /// A custom theme, when the user has edited one. Null means the theme
    /// identified by ThemeId is a built-in used as-is.
    /// </summary>
    public Theming.ThemeTokens? CustomTheme { get; set; }

    /// <summary>
    /// Section ids in published-output order. May legitimately differ from
    /// the authoring order in <see cref="Sections"/>. Ids not present here
    /// fall back to authoring order, appended after the ordered ones.
    /// </summary>
    public List<Guid> SectionDisplayOrder { get; set; } = new();

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ModifiedUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public double TotalPoints => Sections.Sum(s => s.Questions.Sum(q => q.Points));

    [JsonIgnore]
    public int QuestionCount => Sections.Sum(s => s.Questions.Count);

    /// <summary>
    /// Sections in published order, tolerating a stale or partial
    /// SectionDisplayOrder (ids that no longer exist are skipped; sections
    /// missing from the list are appended in authoring order).
    /// </summary>
    public IEnumerable<Section> SectionsInDisplayOrder()
    {
        var byId = Sections.ToDictionary(s => s.Id);
        var seen = new HashSet<Guid>();

        foreach (var id in SectionDisplayOrder)
        {
            if (byId.TryGetValue(id, out var section) && seen.Add(id))
                yield return section;
        }

        foreach (var section in Sections)
        {
            if (seen.Add(section.Id))
                yield return section;
        }
    }
}
