using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class DowsingRodStatsTests
{
    private const string DowsingRodRelicId = "RELIC.DOWSING_ROD";

    private static readonly MethodInfo BuildDowsingRodBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildDowsingRodBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildDowsingRodBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void DowsingPatch_TargetsRoomsEnteredSetter()
    {
        var targetMethod = typeof(DowsingRoomsEnteredStatsPatch).GetMethod(
            "TargetMethod",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("TargetMethod not found.");
        var target = targetMethod.Invoke(null, null) as MethodBase;

        Assert.NotNull(target);
        Assert.Equal("set_RoomsEntered", target!.Name);
        var parameter = Assert.Single(target.GetParameters());
        Assert.Equal("value", parameter.Name);
        Assert.Equal(typeof(int), parameter.ParameterType);
    }

    [Fact]
    public void RelicAggregate_DowsingRoomsRemaining_DefaultsToUnobserved()
    {
        Assert.Null(new RelicAggregate().DowsingQuestionRoomsRemaining);
    }

    [Fact]
    public void RelicAggregate_DowsingRoomsRemaining_JsonRoundtrip_PreservesField()
    {
        var run = new RunData();
        run.RelicAggregates[DowsingRodRelicId] = new RelicAggregate
        {
            DowsingQuestionRoomsRemaining = 2,
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("dowsing_question_rooms_remaining", json);
        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        Assert.Equal(
            2,
            restored!.RelicAggregates[DowsingRodRelicId].DowsingQuestionRoomsRemaining);
    }

    [Fact]
    public void RunTracker_DowsingRoomsEntered_CountsDownFromFiveAndClamps()
    {
        var agg = new RelicAggregate();

        Assert.True(RunTracker.RecordDowsingRoomsEnteredForTest(agg, 0));
        Assert.Equal(5, agg.DowsingQuestionRoomsRemaining);
        Assert.True(RunTracker.RecordDowsingRoomsEnteredForTest(agg, 1));
        Assert.Equal(4, agg.DowsingQuestionRoomsRemaining);
        Assert.True(RunTracker.RecordDowsingRoomsEnteredForTest(agg, 4));
        Assert.Equal(1, agg.DowsingQuestionRoomsRemaining);
        Assert.True(RunTracker.RecordDowsingRoomsEnteredForTest(agg, 5));
        Assert.Equal(0, agg.DowsingQuestionRoomsRemaining);
        Assert.False(RunTracker.RecordDowsingRoomsEnteredForTest(agg, 8));
        Assert.Equal(0, agg.DowsingQuestionRoomsRemaining);
    }

    [Fact]
    public void RelicTooltip_DowsingRod_ShowsRoomsRemainingIncludingInitialFive()
    {
        var initialBody = BuildBody(new RelicAggregate());
        Assert.Contains("? rooms remaining", initialBody);
        Assert.Contains("[b]5[/b]", initialBody);

        var body = BuildBody(new RelicAggregate
        {
            DowsingQuestionRoomsRemaining = 2,
        });
        Assert.Contains("? rooms remaining", body);
        Assert.Contains("[b]2[/b]", body);
    }

    private static string BuildBody(RelicAggregate agg)
    {
        return (string)(BuildDowsingRodBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildDowsingRodBodyBBCode returned null."));
    }
}
