using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Tests for Blood Vial relic stat data model and persistence.
/// Live RunTracker integration is exercised by STS2 verification.
/// </summary>
public class BloodVialStatsTests
{
    private const string BloodVialRelicId = "RELIC.BLOOD_VIAL";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_HpHealed_DefaultsToZero()
    {
        var agg = new RelicAggregate();
        Assert.Equal(0, agg.HpHealed);
    }

    [Fact]
    public void RelicAggregate_HpHealed_JsonRoundtrip_PreservesField()
    {
        var agg = new RelicAggregate { HpHealed = 6 };
        var run = new RunData();
        run.RelicAggregates[BloodVialRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("relic_aggregates", json);
        Assert.Contains("hp_healed", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);
        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(BloodVialRelicId));
        Assert.Equal(6, restored.RelicAggregates[BloodVialRelicId].HpHealed);
    }

    [Fact]
    public void RelicAggregate_HpHealed_AccumulatesAcrossCombats()
    {
        var run = new RunData();

        if (!run.RelicAggregates.TryGetValue(BloodVialRelicId, out var agg))
        {
            agg = new RelicAggregate();
            run.RelicAggregates[BloodVialRelicId] = agg;
        }

        agg.HpHealed += 2;
        agg.HpHealed += 2;
        agg.HpHealed += 1;

        Assert.Equal(5, run.RelicAggregates[BloodVialRelicId].HpHealed);
    }

    [Fact]
    public void RunData_V16WithoutHpHealed_DeserializesWithZeroDefault()
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
                "RELIC.BLOOD_VIAL": {
                  "enemies_affected": 0,
                  "vulnerable_applied": 0,
                  "weak_applied": 0
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.True(run!.RelicAggregates.ContainsKey(BloodVialRelicId));
        Assert.Equal(0, run.RelicAggregates[BloodVialRelicId].HpHealed);
    }
}
