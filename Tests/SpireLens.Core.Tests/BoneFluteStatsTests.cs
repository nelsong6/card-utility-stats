using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class BoneFluteStatsTests
{
    private const string BoneFluteRelicId = "RELIC.BONE_FLUTE";

    private static readonly MethodInfo BuildBoneFluteBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildBoneFluteBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildBoneFluteBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_BoneFluteFields_DefaultToZero()
    {
        var agg = new RelicAggregate();
        Assert.Equal(0, agg.BoneFluteTriggers);
        Assert.Equal(0, agg.AdditionalBlockGained);
    }

    [Fact]
    public void RelicAggregate_BoneFluteFields_JsonRoundtrip_PreserveFields()
    {
        var agg = new RelicAggregate { BoneFluteTriggers = 2, AdditionalBlockGained = 14 };
        var run = new RunData();
        run.RelicAggregates[BoneFluteRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("bone_flute_triggers", json);
        Assert.Contains("additional_block_gained", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);
        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(BoneFluteRelicId));
        Assert.Equal(2, restored.RelicAggregates[BoneFluteRelicId].BoneFluteTriggers);
        Assert.Equal(14, restored.RelicAggregates[BoneFluteRelicId].AdditionalBlockGained);
    }

    [Fact]
    public void RelicAggregate_BoneFluteFields_SeparateTriggersFromBlockPayload()
    {
        var run = new RunData();
        var agg = new RelicAggregate();
        run.RelicAggregates[BoneFluteRelicId] = agg;

        agg.BoneFluteTriggers++;
        agg.AdditionalBlockGained += 7;
        agg.BoneFluteTriggers++;
        agg.AdditionalBlockGained += 7;

        Assert.Equal(2, run.RelicAggregates[BoneFluteRelicId].BoneFluteTriggers);
        Assert.Equal(14, run.RelicAggregates[BoneFluteRelicId].AdditionalBlockGained);
    }

    [Fact]
    public void RelicTooltip_BoneFluteFields_ShowTriggerAndBlockPayloadTotals()
    {
        var agg = new RelicAggregate { BoneFluteTriggers = 2, AdditionalBlockGained = 14 };

        var body = (string)(BuildBoneFluteBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildBoneFluteBodyBBCode returned null."));

        Assert.Contains("Times triggered", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("[img=16x16]res://images/ui/combat/block.png[/img] block gained", body);
        Assert.Contains("[b]14[/b]", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutBoneFluteFields_DeserializesWithZeroDefaults()
    {
        const string json = """
            {
              "run_id": "test",
              "started_at": "2026-01-01T00:00:00Z",
              "updated_at": "2026-01-01T00:00:00Z",
              "outcome": "in_progress",
              "aggregates": {},
              "events": [],
              "instance_numbers_by_def": {},
              "def_counters": {},
              "relic_aggregates": {
                "RELIC.BONE_FLUTE": {
                  "enemies_affected": 0,
                  "vulnerable_applied": 0,
                  "weak_applied": 0,
                  "additional_block_gained": 0
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.True(run!.RelicAggregates.ContainsKey(BoneFluteRelicId));
        Assert.Equal(0, run.RelicAggregates[BoneFluteRelicId].BoneFluteTriggers);
        Assert.Equal(0, run.RelicAggregates[BoneFluteRelicId].AdditionalBlockGained);
    }
}
