using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// DocumentTextInventory: the walk that feeds the spell/grammar review. These
/// tests are the C# port of tools/port/text_inventory_port.py and pin the three
/// properties a compiler cannot: coverage (no authored field missed), no
/// machinery leaks (ids/points/ordinals/image paths never surface as text), and
/// round-trip (every setter mutates the exact source location it was read from).
/// </summary>
public class DocumentTextInventoryTests
{
    // ----- builders -------------------------------------------------------- //

    private static List<Question> OneOfEachKind() => new()
    {
        new MultipleChoiceSingleQuestion
        {
            Prompt = "mc1 prompt", Hint = "mc1 hint",
            Choices = { new Choice { Text = "alpha", IsCorrect = true },
                        new Choice { Text = "beta" } }
        },
        new MultipleChoiceMultipleQuestion
        {
            Prompt = "mc2 prompt",
            Choices = { new Choice { Text = "gamma", IsCorrect = true },
                        new Choice { Text = "delta", IsCorrect = true } }
        },
        new TrueFalseQuestion { Prompt = "tf prompt", CorrectAnswer = false },
        new ShortAnswerQuestion
        {
            Prompt = "sa prompt",
            AcceptedAnswers = { "colour", "color" }
        },
        new FillInTheBlankQuestion
        {
            Prompt = "fb prompt with {{1}} and {{2}}",
            Blanks =
            {
                new Blank { Ordinal = 1, AcceptedAnswers = { "epsilon" } },
                new Blank { Ordinal = 2, AcceptedAnswers = { "zeta", "zeeta" } },
            }
        },
        new MatchingQuestion
        {
            Prompt = "match prompt",
            Pairs = { new MatchPair { Left = "leftA", Right = "rightA" },
                      new MatchPair { Left = "leftB", Right = "rightB" } },
            Distractors = { "distract1", "distract2" }
        },
        new SequenceQuestion
        {
            Prompt = "seq prompt",
            Items = { "step one", "step two", "step three" }
        },
        new EssayQuestion { Prompt = "essay prompt", RubricNotes = "rubric here" },
    };

    private static QuizDocument FullDocument()
    {
        var doc = new QuizDocument
        {
            Title = "Sample Quiz",
            Description = "A description with somme misspellings.",
        };
        doc.Sections.Add(new Section { Title = "Section One", Questions = OneOfEachKind() });
        doc.Sections.Add(new Section
        {
            Title = "Section Two",
            Questions = { new TrueFalseQuestion { Prompt = "s2 tf prompt" } } // no hint
        });
        doc.StudyCards.Add(new StudyCard { Front = "Front text", Back = "Back text" });
        doc.StudyCards.Add(new StudyCard { Front = "Term", Back = "Definition" });
        return doc;
    }

    // ----- coverage -------------------------------------------------------- //

    [Fact]
    public void EveryFieldKindAppearsInExactCounts()
    {
        var fields = DocumentTextInventory.Enumerate(FullDocument());

        var counts = fields
            .GroupBy(f => f.Kind)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(1, counts[TextFieldKind.QuizTitle]);
        Assert.Equal(1, counts[TextFieldKind.QuizDescription]);
        Assert.Equal(2, counts[TextFieldKind.SectionTitle]);
        Assert.Equal(9, counts[TextFieldKind.QuestionPrompt]); // 8 in S1 + 1 in S2
        Assert.Equal(9, counts[TextFieldKind.QuestionHint]);   // one per question
        Assert.Equal(4, counts[TextFieldKind.ChoiceText]);     // 2 + 2
        Assert.Equal(2, counts[TextFieldKind.AcceptedAnswer]);
        Assert.Equal(3, counts[TextFieldKind.BlankAnswer]);    // 1 + 2
        Assert.Equal(2, counts[TextFieldKind.MatchLeft]);
        Assert.Equal(2, counts[TextFieldKind.MatchRight]);
        Assert.Equal(2, counts[TextFieldKind.Distractor]);
        Assert.Equal(3, counts[TextFieldKind.SequenceItem]);
        Assert.Equal(1, counts[TextFieldKind.RubricNotes]);
        Assert.Equal(2, counts[TextFieldKind.StudyCardFront]);
        Assert.Equal(2, counts[TextFieldKind.StudyCardBack]);

        Assert.Equal(45, fields.Count);
    }

    [Fact]
    public void EveryAuthoredStringIsReachable()
    {
        var doc = FullDocument();

        var inventoried = DocumentTextInventory.Enumerate(doc)
            .Select(f => f.Text)
            .Where(t => t.Length > 0)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        // Independently gather every authored string by brute force. If a model
        // field is later added but not wired into the walker, this diverges.
        var authored = new List<string> { doc.Title, doc.Description };
        foreach (var s in doc.Sections)
        {
            authored.Add(s.Title);
            foreach (var q in s.Questions)
            {
                authored.Add(q.Prompt);
                if (!string.IsNullOrEmpty(q.Hint)) authored.Add(q.Hint);
                switch (q)
                {
                    case MultipleChoiceSingleQuestion mcs:
                        authored.AddRange(mcs.Choices.Select(c => c.Text)); break;
                    case MultipleChoiceMultipleQuestion mcm:
                        authored.AddRange(mcm.Choices.Select(c => c.Text)); break;
                    case ShortAnswerQuestion sa:
                        authored.AddRange(sa.AcceptedAnswers); break;
                    case FillInTheBlankQuestion fb:
                        authored.AddRange(fb.Blanks.SelectMany(b => b.AcceptedAnswers)); break;
                    case MatchingQuestion mq:
                        authored.AddRange(mq.Pairs.SelectMany(p => new[] { p.Left, p.Right }));
                        authored.AddRange(mq.Distractors); break;
                    case SequenceQuestion sq:
                        authored.AddRange(sq.Items); break;
                    case EssayQuestion eq:
                        if (!string.IsNullOrEmpty(eq.RubricNotes)) authored.Add(eq.RubricNotes!); break;
                }
            }
        }
        foreach (var card in doc.StudyCards)
        {
            authored.Add(card.Front);
            authored.Add(card.Back);
        }

        var expected = authored
            .Where(t => t.Length > 0)
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, inventoried);
    }

    // ----- no machinery leaks --------------------------------------------- //

    [Fact]
    public void MachineryNeverSurfacesAsText()
    {
        var doc = FullDocument();
        doc.Sections[0].Questions[0].ImageRelativePath = "images/SHOULD_NOT_APPEAR.png";
        doc.StudyCards[0].FrontImageRelativePath = "images/ALSO_NOT.png";

        var joined = string.Join(" || ",
            DocumentTextInventory.Enumerate(doc).Select(f => f.Text));

        Assert.DoesNotContain("SHOULD_NOT_APPEAR", joined);
        Assert.DoesNotContain("ALSO_NOT", joined);
        Assert.DoesNotContain("images/", joined);
    }

    // ----- round-trip ------------------------------------------------------ //

    [Fact]
    public void SettersMutateTheExactSourceLocation()
    {
        var doc = FullDocument();
        var fields = DocumentTextInventory.Enumerate(doc);

        // Scalar attribute
        First(fields, TextFieldKind.QuizTitle).Set("Corrected Quiz Title");
        Assert.Equal("Corrected Quiz Title", doc.Title);

        // Optional that started null (S2's question has no hint -> reads "")
        var s2Hint = fields.Where(f => f.Kind == TextFieldKind.QuestionHint).Last();
        Assert.Equal(string.Empty, s2Hint.Text);
        s2Hint.Set("a newly written hint");
        Assert.Equal("a newly written hint", doc.Sections[1].Questions[0].Hint);

        // List element (short-answer accepted answer)
        First(fields, TextFieldKind.AcceptedAnswer).Set("colourFIXED");
        Assert.Equal("colourFIXED",
            ((ShortAnswerQuestion)doc.Sections[0].Questions[3]).AcceptedAnswers[0]);

        // List element deeper in (sequence item index 1)
        fields.Where(f => f.Kind == TextFieldKind.SequenceItem).ElementAt(1).Set("STEP TWO FIXED");
        Assert.Equal("STEP TWO FIXED",
            ((SequenceQuestion)doc.Sections[0].Questions[6]).Items[1]);

        // Matching right side (first pair)
        First(fields, TextFieldKind.MatchRight).Set("rightA-FIXED");
        Assert.Equal("rightA-FIXED",
            ((MatchingQuestion)doc.Sections[0].Questions[5]).Pairs[0].Right);

        // Nested list: fill-in-the-blank second blank, second accepted answer
        fields.Where(f => f.Kind == TextFieldKind.BlankAnswer).Last().Set("zeeta-FIXED");
        Assert.Equal("zeeta-FIXED",
            ((FillInTheBlankQuestion)doc.Sections[0].Questions[4]).Blanks[1].AcceptedAnswers[1]);
    }

    // ----- grouping metadata ---------------------------------------------- //

    [Fact]
    public void GroupingIdsAreAttached()
    {
        var doc = FullDocument();
        var fields = DocumentTextInventory.Enumerate(doc);

        var quizTitle = First(fields, TextFieldKind.QuizTitle);
        Assert.Null(quizTitle.SectionId);
        Assert.Null(quizTitle.QuestionId);

        var choice = First(fields, TextFieldKind.ChoiceText);
        Assert.Equal(doc.Sections[0].Id, choice.SectionId);
        Assert.Equal(doc.Sections[0].Questions[0].Id, choice.QuestionId);

        var sectionTitle = First(fields, TextFieldKind.SectionTitle);
        Assert.Equal(doc.Sections[0].Id, sectionTitle.SectionId);
        Assert.Null(sectionTitle.QuestionId);
    }

    // ----- edge cases ------------------------------------------------------ //

    [Fact]
    public void EmptyDocumentYieldsOnlyTitleAndDescription()
    {
        var fields = DocumentTextInventory.Enumerate(new QuizDocument());

        Assert.Equal(
            new[] { TextFieldKind.QuizTitle, TextFieldKind.QuizDescription },
            fields.Select(f => f.Kind).ToArray());
    }

    [Fact]
    public void TrueFalseContributesPromptAndHintOnly()
    {
        var doc = new QuizDocument();
        doc.Sections.Add(new Section
        {
            Title = "S",
            Questions = { new TrueFalseQuestion { Prompt = "p", Hint = "h", CorrectAnswer = true } }
        });

        var kinds = DocumentTextInventory.Enumerate(doc)
            .Select(f => f.Kind)
            .Where(k => k is not (TextFieldKind.QuizTitle
                              or TextFieldKind.QuizDescription
                              or TextFieldKind.SectionTitle))
            .ToArray();

        Assert.Equal(
            new[] { TextFieldKind.QuestionPrompt, TextFieldKind.QuestionHint },
            kinds);
    }

    [Fact]
    public void NullDocumentThrows() =>
        Assert.Throws<ArgumentNullException>(() => DocumentTextInventory.Enumerate(null!));

    private static TextField First(IReadOnlyList<TextField> fields, TextFieldKind kind) =>
        fields.First(f => f.Kind == kind);
}
