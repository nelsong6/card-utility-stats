using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Tests for Cloak Clasp relic stat data model, persistence, and schema
/// backwards compatibility. Live RunTracker integration is exercised by the
/// verification phase via live in-run MCP evidence.
/// </summary>
public class CloakClaspStatsTests
{
    private const string CloakClaspRelicId = "RELIC.CLOAK_CLASP";

    private static readonly MethodInfo BuildCloakClaspBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildCloakClaspBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildCloakClaspBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_CloakClaspFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.AdditionalBlockGained);
        Assert.Equal(0, agg.CloakClaspTurns);
        Assert.Equal(0, agg.CloakClaspCombats);
    }

    [Fact]
    public void RelicAggregate_CloakClaspFields_JsonRoundtrip_PreservesFields()
    {
        var agg = new RelicAggregate
        {
            AdditionalBlockGained = 21,
            CloakClaspTurns = 7,
            CloakClaspCombats = 3,
        };
        var run = new RunData();
        run.RelicAggregates[CloakClaspRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("relic_aggregates", json);
        Assert.Contains("additional_block_gained", json);
        Assert.Contains("cloak_clasp_turns", json);
        Assert.Contains("cloak_clasp_combats", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);
        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(CloakClaspRelicId));
        var restoredAgg = restored.RelicAggregates[CloakClaspRelicId];
        Assert.Equal(21, restoredAgg.AdditionalBlockGained);
        Assert.Equal(7, restoredAgg.CloakClaspTurns);
        Assert.Equal(3, restoredAgg.CloakClaspCombats);
    }

    [Fact]
    public void RelicAggregate_AdditionalBlockGained_AccumulatesAcrossMultipleTurns()
    {
        var run = new RunData();

        if (!run.RelicAggregates.TryGetValue(CloakClaspRelicId, out var agg))
        {
            agg = new RelicAggregate();
            run.RelicAggregates[CloakClaspRelicId] = agg;
        }

        // 3 cards in hand on turn 1 → 3 block; 4 cards on turn 2 → 4 block; etc.
        agg.AdditionalBlockGained += 3;
        agg.AdditionalBlockGained += 4;
        agg.AdditionalBlockGained += 5;

        Assert.Equal(12, run.RelicAggregates[CloakClaspRelicId].AdditionalBlockGained);
    }

    [Fact]
    public void MergeRelicAggregateInto_CloakClaspFields_Accumulates()
    {
        var target = new RelicAggregate
        {
            AdditionalBlockGained = 8,
            CloakClaspTurns = 3,
            CloakClaspCombats = 1,
        };
        var source = new RelicAggregate
        {
            AdditionalBlockGained = 13,
            CloakClaspTurns = 4,
            CloakClaspCombats = 2,
        };

        RunTracker.MergeRelicAggregateInto(target, source);

        Assert.Equal(21, target.AdditionalBlockGained);
        Assert.Equal(7, target.CloakClaspTurns);
        Assert.Equal(3, target.CloakClaspCombats);
    }

    [Fact]
    public void RecordCloakClaspDenominators_IncludeHeldTurnsAndCombats()
    {
        var agg = new RelicAggregate();

        RunTracker.RecordCloakClaspTurnForTest(agg, 7);
        RunTracker.RecordCloakClaspCombatForTest(agg, 3);
        RunTracker.RecordCloakClaspTurnForTest(agg, -1);
        RunTracker.RecordCloakClaspCombatForTest(agg, -1);

        Assert.Equal(7, agg.CloakClaspTurns);
        Assert.Equal(3, agg.CloakClaspCombats);
    }

    [Fact]
    public void RelicTooltip_CloakClasp_ShowsBlockTotalAndAverages()
    {
        var agg = new RelicAggregate
        {
            AdditionalBlockGained = 21,
            CloakClaspTurns = 7,
            CloakClaspCombats = 3,
        };

        var body = (string)(BuildCloakClaspBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildCloakClaspBodyBBCode returned null."));

        Assert.Contains("[hint=\"Block gained:", body);
        Assert.Contains("Block gained", body);
        Assert.Contains("[b]21[/b]", body);
        Assert.Contains("[hint=\"Average:", body);
        Assert.Contains("[hint=\"Turn:", body);
        Assert.Contains("avg block gained per turn", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[hint=\"Combat:", body);
        Assert.Contains("avg block gained per combat", body);
        Assert.Contains("[b]7[/b]", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutCloakClasp_DeserializesCleanly()
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
                "RELIC.ORICHALCUM": {
                  "enemies_affected": 0,
                  "vulnerable_applied": 0,
                  "weak_applied": 0,
                  "additional_block_gained": 6
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.False(run!.RelicAggregates.ContainsKey(CloakClaspRelicId));
        Assert.Equal(6, run.RelicAggregates["RELIC.ORICHALCUM"].AdditionalBlockGained);
    }

    [Fact]
    public void RunData_OlderCloakClaspShape_DeserializesWithZeroDenominators()
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
                "RELIC.CLOAK_CLASP": {
                  "additional_block_gained": 6
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        var agg = run!.RelicAggregates[CloakClaspRelicId];
        Assert.Equal(6, agg.AdditionalBlockGained);
        Assert.Equal(0, agg.CloakClaspTurns);
        Assert.Equal(0, agg.CloakClaspCombats);
    }
}
