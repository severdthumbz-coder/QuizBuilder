using QuizBuilder.Core.Interfaces;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// The flash card text setting is a multiplier on the theme's type ramp rather
/// than a stored point size, so a theme with a larger base size still moves the
/// cards with it. These pin the range and the default.
/// </summary>
public class FlashCardTextScaleTests
{
    [Fact]
    public void DefaultsToUnscaled()
    {
        // A new install must look exactly as it did before the setting existed.
        Assert.Equal(1.0, new QuizSettings().FlashCardTextScale, 6);
        Assert.Equal(1.0, QuizSettings.FlashCardTextScaleDefault, 6);
    }

    [Fact]
    public void RangeAllowsSmallerAndSubstantiallyLarger()
    {
        Assert.True(QuizSettings.FlashCardTextScaleMin < 1.0);
        Assert.True(QuizSettings.FlashCardTextScaleMax >= 2.0);
    }

    [Fact]
    public void TheStepDividesTheRangeEvenly()
    {
        // Otherwise stepping up from the default cannot land exactly on the
        // maximum, and the last press moves by an odd fraction.
        var span = QuizSettings.FlashCardTextScaleMax - QuizSettings.FlashCardTextScaleMin;
        var steps = span / QuizSettings.FlashCardTextScaleStep;

        Assert.Equal(Math.Round(steps), steps, 6);
    }

    [Fact]
    public void TheDefaultSitsOnAStepBoundary()
    {
        var fromMin = QuizSettings.FlashCardTextScaleDefault - QuizSettings.FlashCardTextScaleMin;
        var steps = fromMin / QuizSettings.FlashCardTextScaleStep;

        Assert.Equal(Math.Round(steps), steps, 6);
    }

    [Theory]
    [InlineData(-5.0, 0.75)]
    [InlineData(0.0, 0.75)]
    [InlineData(1.5, 1.5)]
    [InlineData(99.0, 2.5)]
    public void ClampingKeepsAHandEditedFileReadable(double stored, double expected)
    {
        // The view model reads through a clamp, so a settings file edited by
        // hand cannot produce invisible or comically large cards.
        var clamped = Math.Clamp(
            stored,
            QuizSettings.FlashCardTextScaleMin,
            QuizSettings.FlashCardTextScaleMax);

        Assert.Equal(expected, clamped, 6);
    }

    [Fact]
    public void ScaleSurvivesASettingsRoundTrip()
    {
        var settings = new AppSettings();
        settings.Quiz.FlashCardTextScale = 1.75;

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var back = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(back);
        Assert.Equal(1.75, back!.Quiz.FlashCardTextScale, 6);
    }
}
