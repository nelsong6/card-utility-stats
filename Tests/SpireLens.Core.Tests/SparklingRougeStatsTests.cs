using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class SparklingRougeStatsTests
{
    private const string SparklingRougeRelicId = "RELIC.SPARKLING_ROUGE";

    private static readonly MethodInfo BuildSparklingRougeBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod(
            "BuildSparklingRougeBodyBBCode",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException(
            "BuildSparklingRougeBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_SparklingRougeFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.SparklingRougeCombatsEndedOnTurn1);
        Assert.Equal(0, agg.SparklingRougeCombatsEndedOnTurn2);
        Assert.Equal(0, agg.SparklingRougeCombatsEndedOnTurn3Plus);
    }

    [Fact]
    public void RecordCombatEnd_AssignsExactlyOneTurnBucket()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordSparklingRougeCombatEndForTest(agg, 0);
        RunTracker.RecordSparklingRougeCombatEndForTest(agg, 1);
        RunTracker.RecordSparklingRougeCombatEndForTest(agg, 2);
        RunTracker.RecordSparklingRougeCombatEndForTest(agg, 3);
        RunTracker.RecordSparklingRougeCombatEndForTest(agg, 7);

        Assert.Equal(1, agg.SparklingRougeCombatsEndedOnTurn1);
        Assert.Equal(1, agg.SparklingRougeCombatsEndedOnTurn2);
        Assert.Equal(2, agg.SparklingRougeCombatsEndedOnTurn3Plus);
    }

    [Fact]
    public void RelicAggregate_SparklingRougeFields_JsonRoundtrip_PreservesFields()
    {
        var run = new RunData();
        run.RelicAggregates[SparklingRougeRelicId] = PopulatedAggregate();

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("\"sparkling_rouge_combats_ended_on_turn1\":2", json);
        Assert.Contains("\"sparkling_rouge_combats_ended_on_turn2\":3", json);
        Assert.Contains("\"sparkling_rouge_combats_ended_on_turn3_plus\":4", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        var agg = restored!.RelicAggregates[SparklingRougeRelicId];
        Assert.Equal(2, agg.SparklingRougeCombatsEndedOnTurn1);
        Assert.Equal(3, agg.SparklingRougeCombatsEndedOnTurn2);
        Assert.Equal(4, agg.SparklingRougeCombatsEndedOnTurn3Plus);
    }

    [Fact]
    public void RelicAggregate_SparklingRougeFields_Merge()
    {
        var target = PopulatedAggregate();

        RunTracker.MergeRelicAggregateInto(target, PopulatedAggregate());

        Assert.Equal(4, target.SparklingRougeCombatsEndedOnTurn1);
        Assert.Equal(6, target.SparklingRougeCombatsEndedOnTurn2);
        Assert.Equal(8, target.SparklingRougeCombatsEndedOnTurn3Plus);
    }

    [Fact]
    public void RelicTooltip_SparklingRouge_ShowsCombatEndTurnBuckets()
    {
        var body = (string)(BuildSparklingRougeBodyMethod.Invoke(
                null,
                new object?[] { PopulatedAggregate() })
            ?? throw new InvalidOperationException(
                "BuildSparklingRougeBodyBBCode returned null."));

        Assert.Contains("Combats ended on turn 1", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("Combats ended on turn 2", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("Combats ended on turn 3+", body);
        Assert.Contains("[b]4[/b]", body);
    }

    private static RelicAggregate PopulatedAggregate()
        => new()
        {
            SparklingRougeCombatsEndedOnTurn1 = 2,
            SparklingRougeCombatsEndedOnTurn2 = 3,
            SparklingRougeCombatsEndedOnTurn3Plus = 4,
        };
}
