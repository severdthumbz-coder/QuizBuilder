using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// GrammarReviewEngine: the prompt builder + resilient response parser for the
/// AI grammar review. C# port of tools/port/grammar_prompt_parse_port.py. The
/// network call is App-only; everything here is pure and runs on a plain runner.
/// The parser's job is to survive messy model output and NEVER surface a
/// suggestion it can't anchor to real source text.
/// </summary>
public class GrammarReviewEngineTests
{
    private static IReadOnlyList<GrammarField> Fields() => new[]
    {
        new GrammarField(0, "Question prompt", "Their going to the store tomorrow."),
        new GrammarField(1, "Choice", "A apple a day."),
        new GrammarField(2, "Hint", "This sentence is perfectly fine."),
    };

    // ----- prompt building ------------------------------------------------- //

    [Fact]
    public void PromptIncludesOnlyNonEmptyFields()
    {
        var fields = new[]
        {
            new GrammarField(0, "Prompt", "hello"),
            new GrammarField(1, "Hint", "   "),
            new GrammarField(2, "Choice", "world"),
        };
        var prompt = GrammarReviewEngine.BuildUserPrompt(fields);
        Assert.Contains("[0]", prompt);
        Assert.Contains("[2]", prompt);
        Assert.DoesNotContain("[1]", prompt);
    }

    [Fact]
    public void HasCheckableTextReflectsContent()
    {
        Assert.True(GrammarReviewEngine.HasCheckableText(Fields()));
        Assert.False(GrammarReviewEngine.HasCheckableText(new[]
        {
            new GrammarField(0, "Prompt", ""),
            new GrammarField(1, "Hint", "   "),
        }));
    }

    // ----- parsing: happy shapes ------------------------------------------- //

    [Fact]
    public void CleanJsonArrayParsedAndAnchored()
    {
        var raw = "[{\"field\":0,\"original\":\"Their going\",\"rewrite\":\"They're going\",\"reason\":\"contraction\"}]";
        var r = GrammarReviewEngine.ParseResponse(raw, Fields());
        Assert.True(r.Success);
        var s = Assert.Single(r.Suggestions);
        Assert.Equal(0, s.FieldId);
        Assert.Equal("Their going", s.Original);
        Assert.Equal("They're going", s.Rewrite);
        Assert.Equal(0, s.Start);
        Assert.Equal("Their going".Length, s.Length);
    }

    [Fact]
    public void FencedJsonExtracted()
    {
        var raw = "```json\n[{\"field\":1,\"original\":\"A apple\",\"rewrite\":\"An apple\",\"reason\":\"article\"}]\n```";
        var r = GrammarReviewEngine.ParseResponse(raw, Fields());
        Assert.True(r.Success);
        var s = Assert.Single(r.Suggestions);
        Assert.Equal(1, s.FieldId);
        Assert.Equal("An apple", s.Rewrite);
    }

    [Fact]
    public void ProseWrappedJsonExtracted()
    {
        var raw = "Sure! Here are the issues I found:\n[{\"field\":0,\"original\":\"Their\",\"rewrite\":\"They're\",\"reason\":\"x\"}]\nHope this helps!";
        var r = GrammarReviewEngine.ParseResponse(raw, Fields());
        Assert.True(r.Success);
        Assert.Equal("Their", Assert.Single(r.Suggestions).Original);
    }

    [Fact]
    public void ObjectWrapperUnwrapped()
    {
        var raw = "{\"suggestions\":[{\"field\":1,\"original\":\"A apple\",\"rewrite\":\"An apple\",\"reason\":\"x\"}]}";
        var r = GrammarReviewEngine.ParseResponse(raw, Fields());
        Assert.True(r.Success);
        Assert.Single(r.Suggestions);
    }

    [Fact]
    public void StringFieldIdCoerced()
    {
        var raw = "[{\"field\":\"0\",\"original\":\"Their\",\"rewrite\":\"They're\",\"reason\":\"x\"}]";
        var r = GrammarReviewEngine.ParseResponse(raw, Fields());
        Assert.True(r.Success);
        Assert.Equal(0, Assert.Single(r.Suggestions).FieldId);
    }

    // ----- parsing: success-with-nothing and errors ------------------------ //

    [Fact]
    public void EmptyArrayIsSuccessNotError()
    {
        var r = GrammarReviewEngine.ParseResponse("[]", Fields());
        Assert.True(r.Success);
        Assert.Empty(r.Suggestions);
        Assert.Null(r.Message);
    }

    [Fact]
    public void MalformedJsonIsCleanError()
    {
        var r = GrammarReviewEngine.ParseResponse("[{\"field\":0,\"original\":\"Their\",", Fields());
        Assert.False(r.Success);
        Assert.Empty(r.Suggestions);
        Assert.Contains("JSON", r.Message);
    }

    [Fact]
    public void EmptyResponseIsCleanError()
    {
        var r = GrammarReviewEngine.ParseResponse("", Fields());
        Assert.False(r.Success);
        Assert.Contains("Empty", r.Message);
    }

    // ----- parsing: safety drops ------------------------------------------- //

    [Fact]
    public void HallucinatedOriginalIsDropped()
    {
        // field 2 does not contain this phrase — must not be surfaced
        var raw = "[{\"field\":2,\"original\":\"nonexistent phrase\",\"rewrite\":\"whatever\",\"reason\":\"x\"}]";
        var r = GrammarReviewEngine.ParseResponse(raw, Fields());
        Assert.True(r.Success);
        Assert.Empty(r.Suggestions);
    }

    [Fact]
    public void UnknownFieldIsDropped()
    {
        var raw = "[{\"field\":99,\"original\":\"whatever\",\"rewrite\":\"x\",\"reason\":\"y\"}]";
        var r = GrammarReviewEngine.ParseResponse(raw, Fields());
        Assert.True(r.Success);
        Assert.Empty(r.Suggestions);
    }

    [Fact]
    public void NoOpSuggestionIsDropped()
    {
        var raw = "[{\"field\":0,\"original\":\"Their going\",\"rewrite\":\"Their going\",\"reason\":\"none\"}]";
        var r = GrammarReviewEngine.ParseResponse(raw, Fields());
        Assert.True(r.Success);
        Assert.Empty(r.Suggestions);
    }

    [Fact]
    public void WhitespaceTolerantAnchorMapsToRealSpan()
    {
        var fields = new[] { new GrammarField(0, "Prompt", "the  quick   brown fox") };
        var raw = "[{\"field\":0,\"original\":\"quick brown\",\"rewrite\":\"swift brown\",\"reason\":\"x\"}]";
        var r = GrammarReviewEngine.ParseResponse(raw, fields);
        Assert.True(r.Success);
        var s = Assert.Single(r.Suggestions);
        // anchored to the real, irregularly-spaced source span
        Assert.Equal("quick   brown", fields[0].Text.Substring(s.Start, s.Length));
        Assert.Equal("quick   brown", s.Original);
    }
}
