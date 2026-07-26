using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// settings.json is hand-editable, so the clamp has to hold on read as well as
/// on write. A pass mark of 500 in a hand-edited file would otherwise make
/// every paper unpassable with no clue why.
/// </summary>
public class PassPercentageTests
{
    [Fact]
    public void DefaultsToFifty()
        => Assert.Equal(50, new QuizSettings().PassPercentage);

    [Fact]
    public void ClampsAboveOneHundredOnWrite()
    {
        var settings = new QuizSettings { PassPercentage = 500 };

        Assert.Equal(100, settings.PassPercentage);
    }

    [Fact]
    public void ClampsBelowZeroOnWrite()
    {
        var settings = new QuizSettings { PassPercentage = -20 };

        Assert.Equal(0, settings.PassPercentage);
    }

    [Fact]
    public void AcceptsTheBoundaries()
    {
        Assert.Equal(0, new QuizSettings { PassPercentage = 0 }.PassPercentage);
        Assert.Equal(100, new QuizSettings { PassPercentage = 100 }.PassPercentage);
    }

    [Fact]
    public void SurvivesAJsonRoundTrip()
    {
        var settings = new QuizSettings { PassPercentage = 65 };

        // The app's own options, not fresh ones: a test that round-trips with
        // different settings than production proves nothing about production.
        var json = System.Text.Json.JsonSerializer.Serialize(settings, SettingsService.JsonOptions);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<QuizSettings>(
            json, SettingsService.JsonOptions);

        Assert.NotNull(loaded);
        Assert.Equal(65, loaded!.PassPercentage);
    }

    [Fact]
    public void PassMarkBasis_PersistsAsAName_NotAnOrdinal()
    {
        var settings = new QuizSettings { PassMarkBasis = PassMarkBasis.TotalPoints };

        var json = System.Text.Json.JsonSerializer.Serialize(settings, SettingsService.JsonOptions);

        // Stored as a name. As an ordinal, adding a value to the middle of the
        // enum later would silently flip every existing user's setting.
        Assert.Contains("TotalPoints", json);

        var loaded = System.Text.Json.JsonSerializer.Deserialize<QuizSettings>(
            json, SettingsService.JsonOptions);

        Assert.Equal(PassMarkBasis.TotalPoints, loaded!.PassMarkBasis);
    }

    [Fact]
    public void SettingsThatPredateTheBasisField_DefaultToQuestionCount()
    {
        var json = """{"passPercentage":70}""";

        var loaded = System.Text.Json.JsonSerializer.Deserialize<QuizSettings>(
            json, SettingsService.JsonOptions);

        Assert.NotNull(loaded);
        Assert.Equal(70, loaded!.PassPercentage);
        Assert.Equal(PassMarkBasis.QuestionCount, loaded.PassMarkBasis);
    }

    [Fact]
    public void AHandEditedOutOfRangeValueIsClampedOnLoad()
    {
        // Someone opens settings.json and types 500.
        var json = """{"passPercentage":500}""";

        var loaded = System.Text.Json.JsonSerializer.Deserialize<QuizSettings>(
            json, SettingsService.JsonOptions);

        Assert.NotNull(loaded);
        Assert.Equal(100, loaded!.PassPercentage);
    }

    [Fact]
    public void DocumentsThatPredateTheField_GetTheDefault()
    {
        var json = """{"randomizeQuestionOrder":true}""";

        var loaded = System.Text.Json.JsonSerializer.Deserialize<QuizSettings>(
            json, SettingsService.JsonOptions);

        Assert.NotNull(loaded);
        Assert.Equal(50, loaded!.PassPercentage);
    }
}
