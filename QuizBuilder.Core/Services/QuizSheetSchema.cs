namespace QuizBuilder.Core.Services;

/// <summary>
/// The column layout shared by the Excel exporter and importer.
///
/// One row per question, one wide table. The alternative was a sheet per
/// question type -- faithful to the model, but a teacher opening the file sees
/// seven tabs and has to know which one to use, and bulk editing across types
/// becomes impossible. A single table makes the shape obvious at a glance and
/// costs only some empty cells on an essay row.
///
/// Every repeated field gets its OWN COLUMN rather than a delimited list in one
/// cell. That is the whole reason this class exists. The first design packed
/// matching pairs into "Left = Right" and alternatives into "a / b", which
/// looks tidy until someone writes a short answer of "and/or" (two answers,
/// silently) or a distractor of "x, y" (also two). Any delimiter can appear in
/// the content it separates. Columns cannot collide with anything.
/// </summary>
public static class QuizSheetSchema
{
    public const string QuestionsSheetName = "Questions";
    public const string GuideSheetName = "Guide";

    /// <summary>
    /// Cap on choices, accepted answers, blanks and pairs per question.
    ///
    /// Eight is past the point where a paper question is readable anyway, and
    /// an unbounded count would mean an unbounded column count. Anything beyond
    /// this is reported as a warning rather than truncated silently.
    /// </summary>
    public const int MaxOptions = 8;

    /// <summary>Cap on matching distractors.</summary>
    public const int MaxDistractors = 4;

    public const string Section = "Section";
    public const string Type = "Type";
    public const string Prompt = "Prompt";
    public const string Points = "Points";
    public const string Hint = "Hint";
    public const string Extra = "Extra";

    public static string Option(int n) => $"Option {n}";
    public static string Correct(int n) => $"Correct {n}";
    public static string Match(int n) => $"Match {n}";
    public static string Distractor(int n) => $"Distractor {n}";

    /// <summary>Headers in the order they are written.</summary>
    public static IReadOnlyList<string> Headers { get; } = BuildHeaders();

    private static List<string> BuildHeaders()
    {
        var headers = new List<string> { Section, Type, Prompt, Points, Hint };

        for (var i = 1; i <= MaxOptions; i++) headers.Add(Option(i));
        for (var i = 1; i <= MaxOptions; i++) headers.Add(Correct(i));
        for (var i = 1; i <= MaxOptions; i++) headers.Add(Match(i));
        for (var i = 1; i <= MaxDistractors; i++) headers.Add(Distractor(i));

        headers.Add(Extra);

        return headers;
    }

    /// <summary>
    /// The Guide sheet's contents. Written into the workbook rather than kept in
    /// a README, because the person editing the spreadsheet is looking at the
    /// spreadsheet.
    /// </summary>
    public static IReadOnlyList<(string Heading, string Body)> Guide { get; } = new[]
    {
        ("How this works",
            "One row per question. Edit here, then import the file back into Quiz Builder. "
            + "Column order does not matter and extra columns are ignored, so you can add your own notes."),

        ("Section",
            "The section this question belongs to. Rows with the same section name are grouped together, "
            + "in the order they first appear. A blank section name puts the question in the first section."),

        ("Type",
            "One of: MultipleChoiceSingle, MultipleChoiceMultiple, TrueFalse, ShortAnswer, "
            + "FillInTheBlank, Matching, Sequence, Numeric, Dropdown, Essay. Spaces and capitals are ignored, so \"multiple choice single\" also works."),

        ("Points",
            "A number. Decimals are fine. Zero means the question is not graded, and it is excluded from "
            + "a question-count pass mark."),

        ("MultipleChoiceSingle / MultipleChoiceMultiple",
            "Put each choice in Option 1..8. Put TRUE in the matching Correct column for the right answer, "
            + "or answers. Leave the rest of the Correct columns blank."),

        ("TrueFalse",
            "Put TRUE or FALSE in Correct 1. The Option columns are not used."),

        ("ShortAnswer",
            "Put each accepted answer in Option 1..8. Any one of them counts as correct."),

        ("Dropdown",
            "Same as MultipleChoiceSingle: put each choice in Option 1..8 and TRUE in the matching "
            + "Correct column for the right answer. The taker picks it from a dropdown."),

        ("Numeric",
            "Put the correct number in Option 1. Optionally put a tolerance (how far off still counts) "
            + "in Option 2 — leave it blank or 0 for an exact match. Put a unit label (e.g. kg) in Extra if you want one shown."),

        ("FillInTheBlank",
            "Write the prompt with {{1}}, {{2}} and so on where the blanks go. "
            + "Put the answer for blank 1 in Option 1, blank 2 in Option 2, and so on. "
            + "The spreadsheet holds one answer per blank -- if you need alternatives, add them in the app afterwards."),

        ("Matching",
            "Put the left side in Option 1..8 and its pair in the Match column on the same row. "
            + "Extra unmatched options go in Distractor 1..4."),

        ("Sequence",
            "Put the items in Option 1..8 in their correct order. The order you type them in is the "
            + "answer; the app shuffles them for the taker to rearrange."),

        ("Essay",
            "The prompt is the question. Put any marking notes in Extra."),

        ("Hint",
            "Optional, any type. Shown to the student under the question."),
    };
}
