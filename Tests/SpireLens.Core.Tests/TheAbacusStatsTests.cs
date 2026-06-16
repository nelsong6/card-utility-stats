using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Tests for The Abacus relic stat data model, persistence, and schema
/// backwards compatibility. Live RunTracker integration is exercised by the
/// verification phase via live in-run MCP evidence.
/// </summary>
public class TheAbacusStatsTests
{
    private const string TheAbacusRelicId = "RELIC.THE_ABACUS";

    private static readonly MethodInfo BuildTheAbacusBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildTheAbacusBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildTheAbacusBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_AdditionalBlockGained_DefaultsToZero()
    {
        var agg = new RelicAggregate();
        Assert.Equal(0, agg.AdditionalBlockGained);
    }

    [Fact]
    public void RelicAggregate_AdditionalBlockGained_JsonRoundtrip_PreservesField()
    {
        var agg = new RelicAggregate { AdditionalBlockGained = 18 };
        var run = new RunData();
        run.RelicAggregates[TheAbacusRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("relic_aggregates", json);
        Assert.Contains("additional_block_gained", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);
        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(TheAbacusRelicId));
        var restoredAgg = restored.RelicAggregates[TheAbacusRelicId];
        Assert.Equal(18, restoredAgg.AdditionalBlockGained);
    }

    [Fact]
    public void RelicAggregate_AdditionalBlockGained_AccumulatesAcrossShuffles()
    {
        var run = new RunData();

        if (!run.RelicAggregates.TryGetValue(TheAbacusRelicId, out var agg))
        {
            agg = new RelicAggregate();
            run.RelicAggregates[TheAbacusRelicId] = agg;
        }

        agg.AdditionalBlockGained += 6;
        agg.AdditionalBlockGained += 6;
        agg.AdditionalBlockGained += 6;

        Assert.Equal(18, run.RelicAggregates[TheAbacusRelicId].AdditionalBlockGained);
    }

    [Fact]
    public void RelicTooltip_AdditionalBlockGained_ShowsBlockIconAndTotal()
    {
        var agg = new RelicAggregate { AdditionalBlockGained = 12 };

        var body = (string)(BuildTheAbacusBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildTheAbacusBodyBBCode returned null."));

        Assert.Contains("[img=16x16]res://images/ui/combat/block.png[/img] block gained", body);
        Assert.Contains("[b]12[/b]", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutAdditionalBlockGained_DeserializesWithZeroDefault()
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
                "RELIC.THE_ABACUS": {
                  "enemies_affected": 0,
                  "vulnerable_applied": 0,
                  "weak_applied": 0
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.True(run!.RelicAggregates.ContainsKey(TheAbacusRelicId));
        var agg = run.RelicAggregates[TheAbacusRelicId];
        Assert.Equal(0, agg.AdditionalBlockGained);
    }
}
