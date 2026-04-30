using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

/// <summary>
/// Tests for Akabeko relic stat data model, persistence, and schema
/// backwards compatibility. Live RunTracker integration is exercised by the
/// verification phase via live in-run MCP evidence.
/// </summary>
public class AkabeloStatsTests
{
    private const string AkabeloRelicId = "RELIC.AKABEKO";

    private static readonly MethodInfo BuildAkabeloBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildAkabeloBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildAkabeloBodyBBCode not found.");

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
    public void RelicAggregate_VigorGained_JsonRoundtrip_PreservesField()
    {
        var agg = new RelicAggregate { VigorGained = 8 };
        var run = new RunData();
        run.RelicAggregates[AkabeloRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("relic_aggregates", json);
        Assert.Contains("vigor_gained", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);
        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(AkabeloRelicId));
        var restoredAgg = restored.RelicAggregates[AkabeloRelicId];
        Assert.Equal(8, restoredAgg.VigorGained);
    }

    [Fact]
    public void RelicAggregate_VigorGained_AccumulatesAcrossCombats()
    {
        var run = new RunData();

        if (!run.RelicAggregates.TryGetValue(AkabeloRelicId, out var agg))
        {
            agg = new RelicAggregate();
            run.RelicAggregates[AkabeloRelicId] = agg;
        }

        agg.VigorGained += 8;
        agg.VigorGained += 8;
        agg.VigorGained += 8;

        Assert.Equal(24, run.RelicAggregates[AkabeloRelicId].VigorGained);
    }

    [Fact]
    public void RelicTooltip_VigorGained_ShowsVigorIconAndTotal()
    {
        var agg = new RelicAggregate { VigorGained = 24 };

        var body = (string)(BuildAkabeloBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildAkabeloBodyBBCode returned null."));

        Assert.Contains("[img=16x16]res://images/atlases/power_atlas.sprites/vigor_power.tres[/img] vigor gained", body);
        Assert.Contains("[b]24[/b]", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutVigorGained_DeserializesWithZeroDefault()
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
                "RELIC.AKABEKO": {
                  "enemies_affected": 0,
                  "vulnerable_applied": 0,
                  "weak_applied": 0
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.True(run!.RelicAggregates.ContainsKey(AkabeloRelicId));
        var agg = run.RelicAggregates[AkabeloRelicId];
        Assert.Equal(0, agg.VigorGained);
    }
}
