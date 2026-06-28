using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Tests for Happy Flower relic stat data model, persistence, and tooltip
/// presentation. Live RunTracker integration is exercised by the verification
/// phase via live in-run MCP evidence.
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
        var agg = new RelicAggregate { EnergyGenerated = 3 };
        var run = new RunData();
        run.RelicAggregates[HappyFlowerRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("relic_aggregates", json);
        Assert.Contains("energy_generated", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);
        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(HappyFlowerRelicId));
        var restoredAgg = restored.RelicAggregates[HappyFlowerRelicId];
        Assert.Equal(3, restoredAgg.EnergyGenerated);
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
        var agg = new RelicAggregate { EnergyGenerated = 3 };

        var body = (string)(BuildHappyFlowerBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildHappyFlowerBodyBBCode returned null."));

        Assert.Contains("Energy generated", body);
        Assert.Contains("[b]3[/b]", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutEnergyGenerated_DeserializesWithZeroDefault()
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
                "RELIC.HAPPY_FLOWER": {
                  "enemies_affected": 0,
                  "vulnerable_applied": 0,
                  "weak_applied": 0
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.True(run!.RelicAggregates.ContainsKey(HappyFlowerRelicId));
        var agg = run.RelicAggregates[HappyFlowerRelicId];
        Assert.Equal(0, agg.EnergyGenerated);
    }
}
