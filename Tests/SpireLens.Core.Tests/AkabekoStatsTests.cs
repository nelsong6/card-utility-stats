using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Tests for Akabeko relic stat data model, persistence, and schema
/// backwards compatibility. Live RunTracker integration is exercised by the
/// verification phase via live in-run MCP evidence.
/// </summary>
public class AkabekoStatsTests
{
    private const string AkabekoRelicId = "RELIC.AKABEKO";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_VigorGained_DefaultsToZero()
    {
        var agg = new RelicAggregate();
        Assert.Equal(0, agg.VigorGained);
    }

    [Fact]
    public void RelicAggregate_JsonRoundtrip_PreservesVigorGained()
    {
        var agg = new RelicAggregate { VigorGained = 8 };
        var run = new RunData();
        run.RelicAggregates[AkabekoRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("relic_aggregates", json);
        Assert.Contains("vigor_gained", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);
        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(AkabekoRelicId));
        var restoredAgg = restored.RelicAggregates[AkabekoRelicId];
        Assert.Equal(8, restoredAgg.VigorGained);
    }

    [Fact]
    public void RelicAggregate_VigorGained_AccumulatesAcrossCombats()
    {
        var run = new RunData();

        if (!run.RelicAggregates.TryGetValue(AkabekoRelicId, out var agg))
        {
            agg = new RelicAggregate();
            run.RelicAggregates[AkabekoRelicId] = agg;
        }

        agg.VigorGained += 8;
        agg.VigorGained += 8;
        agg.VigorGained += 8;

        Assert.Equal(24, run.RelicAggregates[AkabekoRelicId].VigorGained);
    }

    [Fact]
    public void RunData_WithoutRelicAggregates_DeserializesWithZeroVigorGained()
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
              "def_counters": {}
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.NotNull(run!.RelicAggregates);
        Assert.Empty(run.RelicAggregates);
    }

    [Fact]
    public void RunData_OlderSchema_WithoutVigorGained_DeserializesAsZero()
    {
        const string json = """
            {
              "run_id": "test",
              "started_at": "2026-01-01T00:00:00Z",
              "updated_at": "2026-01-01T00:00:00Z",
              "outcome": "in_progress",
              "aggregates": {},
              "events": [],
              "relic_aggregates": {
                "RELIC.AKABEKO": {
                  "enemies_affected": 0,
                  "vulnerable_applied": 0,
                  "weak_applied": 0,
                  "additional_cards_drawn": 0,
                  "additional_block_gained": 0
                }
              },
              "instance_numbers_by_def": {},
              "def_counters": {}
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.True(run!.RelicAggregates.ContainsKey(AkabekoRelicId));
        Assert.Equal(0, run.RelicAggregates[AkabekoRelicId].VigorGained);
    }
}
