using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Tests for Happy Flower relic stat data model, persistence, and schema
/// backwards compatibility. Live RunTracker integration is exercised by the
/// verification phase via live in-run MCP evidence.
/// </summary>
public class HappyFlowerStatsTests
{
    private const string HappyFlowerRelicId = "RELIC.HAPPY_FLOWER";

    private static readonly MethodInfo BuildHappyFlowerBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildHappyFlowerBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildHappyFlowerBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_EnergyGenerated_DefaultsToZero()
    {
        var agg = new RelicAggregate();
        Assert.Equal(0, agg.EnergyGenerated);
    }

    [Fact]
    public void RelicAggregate_EnergyGenerated_JsonRoundtrip_PreservesField()
    {
        var agg = new RelicAggregate { EnergyGenerated = 5 };
        var run = new RunData();
        run.RelicAggregates[HappyFlowerRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("relic_aggregates", json);
        Assert.Contains("energy_generated", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);
        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(HappyFlowerRelicId));
        Assert.Equal(5, restored.RelicAggregates[HappyFlowerRelicId].EnergyGenerated);
    }

    [Fact]
    public void RelicAggregate_EnergyGenerated_AccumulatesAcrossTriggers()
    {
        var run = new RunData();

        if (!run.RelicAggregates.TryGetValue(HappyFlowerRelicId, out var agg))
        {
            agg = new RelicAggregate();
            run.RelicAggregates[HappyFlowerRelicId] = agg;
        }

        agg.EnergyGenerated += 1;
        agg.EnergyGenerated += 1;
        agg.EnergyGenerated += 1;

        Assert.Equal(3, run.RelicAggregates[HappyFlowerRelicId].EnergyGenerated);
    }

    [Fact]
    public void RelicTooltip_EnergyGenerated_ShowsEnergyIconAndTotal()
    {
        var agg = new RelicAggregate { EnergyGenerated = 4 };

        var body = (string)(BuildHappyFlowerBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildHappyFlowerBodyBBCode returned null."));

        Assert.Contains("[img=16x16]res://images/atlases/potion_atlas.sprites/energy_potion.tres[/img] energy generated", body);
        Assert.Contains("[b]4[/b]", body);
    }

    [Fact]
    public void RunData_V18WithoutEnergyGenerated_DeserializesWithZeroDefault()
    {
        const string json = """
            {
              "schema_version": 18,
              "run_id": "test",
              "started_at": "2026-01-01T00:00:00Z",
              "updated_at": "2026-01-01T00:00:00Z",
              "outcome": "in_progress",
              "aggregates": {},
              "events": [],
              "instance_numbers_by_def": {},
              "def_counters": {},
              "relic_aggregates": {
                "RELIC.HAPPY_FLOWER": {
                  "enemies_affected": 0,
                  "vulnerable_applied": 0,
                  "weak_applied": 0,
                  "additional_cards_drawn": 0,
                  "additional_block_gained": 0
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.True(run!.RelicAggregates.ContainsKey(HappyFlowerRelicId));
        Assert.Equal(0, run.RelicAggregates[HappyFlowerRelicId].EnergyGenerated);
    }

    [Fact]
    public void RunData_SchemaVersion_IsBumpedTo19()
    {
        Assert.Equal(19, RunData.CurrentSchemaVersion);
    }
}
