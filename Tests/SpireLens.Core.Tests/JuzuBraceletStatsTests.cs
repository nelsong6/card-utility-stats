using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class JuzuBraceletStatsTests
{
    private const string JuzuBraceletRelicId = "RELIC.JUZU_BRACELET";

    private static readonly MethodInfo BuildJuzuBraceletBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildJuzuBraceletBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildJuzuBraceletBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_JuzuBraceletFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.QuestionMarkSitesEntered);
    }

    [Fact]
    public void RelicAggregate_JuzuBraceletFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[JuzuBraceletRelicId] = new RelicAggregate
        {
            QuestionMarkSitesEntered = 3,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("question_mark_sites_entered", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var restoredAgg = restored!.RelicAggregates[JuzuBraceletRelicId];
        Assert.Equal(3, restoredAgg.QuestionMarkSitesEntered);
    }

    [Fact]
    public void RunTracker_JuzuBraceletTestHelper_AccumulatesAndClamps()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordJuzuQuestionSiteEnteredForTest(agg);
        RunTracker.RecordJuzuQuestionSiteEnteredForTest(agg, 2);
        RunTracker.RecordJuzuQuestionSiteEnteredForTest(agg, -1);

        Assert.Equal(3, agg.QuestionMarkSitesEntered);
    }

    [Fact]
    public void RelicTooltip_JuzuBracelet_ShowsQuestionSiteRowIncludingZero()
    {
        var emptyBody = (string)(BuildJuzuBraceletBodyMethod.Invoke(null, new object?[] { new RelicAggregate() })
            ?? throw new InvalidOperationException("BuildJuzuBraceletBodyBBCode returned null."));
        Assert.Contains("? sites entered", emptyBody);
        Assert.Contains("[b]0[/b]", emptyBody);

        var agg = new RelicAggregate
        {
            QuestionMarkSitesEntered = 4,
        };

        var body = (string)(BuildJuzuBraceletBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildJuzuBraceletBodyBBCode returned null."));

        Assert.Contains("? sites entered", body);
        Assert.Contains("[b]4[/b]", body);
    }
}
