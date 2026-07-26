using QuizBuilder.Core.Models;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Ported from a Python reference model that was run against these same cases
/// before any C# existed. The subtle rule is that ordinals bind answers, not
/// positions: editing "The {{1}} sat on the {{2}}" into "The {{2}} held the
/// {{1}}" must keep each answer with its own token rather than swapping them.
/// </summary>
public class BlankSynchroniserTests
{
    private static Blank B(int ordinal, params string[] answers)
        => new() { Ordinal = ordinal, AcceptedAnswers = answers.ToList() };

    [Fact]
    public void NoTokens_ProducesNoBlanks()
    {
        var result = BlankSynchroniser.Sync("Just plain text.", Array.Empty<Blank>());

        Assert.Empty(result.Blanks);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void NoTokens_ButExistingAnswers_Warns()
    {
        var result = BlankSynchroniser.Sync("Plain text now.", new[] { B(1, "cat") });

        Assert.Empty(result.Blanks);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void TwoTokens_ProduceTwoBlanks()
    {
        var result = BlankSynchroniser.Sync("The {{1}} sat on the {{2}}.", Array.Empty<Blank>());

        Assert.Equal(new[] { 1, 2 }, result.Blanks.Select(b => b.Ordinal));
    }

    [Fact]
    public void EditingProse_PreservesAnswers()
    {
        var existing = new[] { B(1, "cat"), B(2, "mat") };

        var result = BlankSynchroniser.Sync("The {{1}} sat on the {{2}} quietly.", existing);

        Assert.Equal("cat", result.Blanks[0].AcceptedAnswers[0]);
        Assert.Equal("mat", result.Blanks[1].AcceptedAnswers[0]);
    }

    [Fact]
    public void ReorderingTokens_KeepsAnswersBoundToOrdinals()
    {
        var existing = new[] { B(1, "cat"), B(2, "mat") };

        // {{1}} now appears second in the sentence, but still means "cat".
        var result = BlankSynchroniser.Sync("The {{2}} held the {{1}}.", existing);

        Assert.Equal("cat", result.Blanks.Single(b => b.Ordinal == 1).AcceptedAnswers[0]);
        Assert.Equal("mat", result.Blanks.Single(b => b.Ordinal == 2).AcceptedAnswers[0]);
    }

    [Fact]
    public void RemovingAToken_DropsItsAnswersAndWarns()
    {
        var existing = new[] { B(1, "cat"), B(2, "mat") };

        var result = BlankSynchroniser.Sync("The {{1}} sat.", existing);

        Assert.Single(result.Blanks);
        Assert.Equal("cat", result.Blanks[0].AcceptedAnswers[0]);
        Assert.Contains(result.Warnings, w => w.Contains("Removed"));
    }

    [Fact]
    public void DuplicateToken_CollapsesAndWarns()
    {
        var result = BlankSynchroniser.Sync("A {{1}} and another {{1}}.", Array.Empty<Blank>());

        Assert.Single(result.Blanks);
        Assert.Contains(result.Warnings, w => w.Contains("more than once"));
    }

    [Fact]
    public void NonContiguousTokens_StillWorkButWarn()
    {
        var result = BlankSynchroniser.Sync("Only {{2}} here.", Array.Empty<Blank>());

        Assert.Single(result.Blanks);
        Assert.Equal(2, result.Blanks[0].Ordinal);
        Assert.Contains(result.Warnings, w => w.Contains("gaps"));
    }

    [Fact]
    public void OutOfOrderTokens_AreSorted()
    {
        var result = BlankSynchroniser.Sync("{{3}} then {{1}}", Array.Empty<Blank>());

        Assert.Equal(new[] { 1, 3 }, result.Blanks.Select(b => b.Ordinal));
    }

    [Fact]
    public void AddingAToken_AppendsAnEmptyBlank()
    {
        var existing = new[] { B(1, "cat"), B(2, "mat") };

        var result = BlankSynchroniser.Sync("The {{1}} sat on the {{2}} near the {{3}}.", existing);

        Assert.Equal(3, result.Blanks.Count);
        Assert.Empty(result.Blanks[2].AcceptedAnswers);
    }

    [Fact]
    public void AppendNextToken_StartsAtOne()
        => Assert.Equal("Fill this {{1}}", BlankSynchroniser.AppendNextToken("Fill this"));

    [Fact]
    public void AppendNextToken_ContinuesFromHighest()
        => Assert.Equal("A {{1}} and {{2}}", BlankSynchroniser.AppendNextToken("A {{1}} and"));

    [Fact]
    public void AppendNextToken_OnEmptyPrompt()
        => Assert.Equal("{{1}}", BlankSynchroniser.AppendNextToken(null));

    [Fact]
    public void TokensIn_HandlesAbsurdNumbersWithoutThrowing()
    {
        // int.Parse would throw on overflow; TryParse keeps a user mid-typing
        // from crashing the editor.
        var tokens = BlankSynchroniser.TokensIn("{{99999999999999999999}}");
        Assert.Empty(tokens);
    }
}
