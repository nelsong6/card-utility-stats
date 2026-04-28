using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Tests for Anchor relic stat data model and persistence.
/// Live RunTracker integration is exercised by STS2 verification.
/// </summary>
public class AnchorStatsTests
{
    private const string AnchorRelicId = "RELIC.ANCHOR";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_BlockGained_DefaultsToZero()
    {
        var agg = new RelicAggregate();
        Assert.Equal(0, agg.BlockGained);
    }

    [Fact]
    public void RelicAggregate_BlockGained_JsonRoundtrip_PreservesFields()
    {
        var agg = new RelicAggregate { BlockGained = 30 };
        var run = new RunData();
        run.RelicAggregates[AnchorRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("relic_aggregates", json);
        Assert.Contains("block_gained", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);
        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(AnchorRelicId));
        var restoredAgg = restored.RelicAggregates[AnchorRelicId];
        Assert.Equal(30, restoredAgg.BlockGained);
    }

    [Fact]
    public void RelicAggregate_BlockGained_AccumulatesAcrossCombats()
    {
        var run = new RunData();

        if (!run.RelicAggregates.TryGetValue(AnchorRelicId, out var agg))
        {
            agg = new RelicAggregate();
            run.RelicAggregates[AnchorRelicId] = agg;
        }

        agg.BlockGained += 10;
        agg.BlockGained += 10;
        agg.BlockGained += 10;

        Assert.Equal(30, run.RelicAggregates[AnchorRelicId].BlockGained);
        Assert.Equal(0, run.RelicAggregates[AnchorRelicId].EnemiesAffected);
    }

    [Fact]
    public void RunData_V16WithoutBlockGained_DeserializesWithZeroDefault()
    {
        const string json = """
            {
              "schema_version": 16,
              "run_id": "test",
              "started_at": "2026-01-01T00:00:00Z",
              "updated_at": "2026-01-01T00:00:00Z",
              "outcome": "in_progress",
              "aggregates": {},
              "events": [],
              "instance_numbers_by_def": {},
              "def_counters": {},
              "relic_aggregates": {
                "RELIC.ANCHOR": {
                  "enemies_affected": 0
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.True(run!.RelicAggregates.ContainsKey(AnchorRelicId));
        var agg = run.RelicAggregates[AnchorRelicId];
        Assert.Equal(0, agg.BlockGained);
    }

    [Fact]
    public void RunData_SchemaVersion_IsBumpedTo17()
    {
        var run = new RunData();
        Assert.Equal(17, RunData.CurrentSchemaVersion);
        Assert.Equal(RunData.CurrentSchemaVersion, run.SchemaVersion);
    }
}
