using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class PhylacteryStatsTests
{
    private const string BoundPhylacteryRelicId = "RELIC.BOUND_PHYLACTERY";

    private static readonly MethodInfo BuildPhylacteryBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildPhylacteryBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildPhylacteryBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_PhylacteryFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.TotalOstyHpSummoned);
    }

    [Fact]
    public void RelicAggregate_PhylacteryFields_JsonRoundtrip_PreserveFields()
    {
        var agg = new RelicAggregate
        {
            Activations = 4,
            TotalOstyHpSummoned = 17m,
        };
        var run = new RunData();
        run.RelicAggregates[BoundPhylacteryRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("total_osty_hp_summoned", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(BoundPhylacteryRelicId));
        var restoredAgg = restored.RelicAggregates[BoundPhylacteryRelicId];
        Assert.Equal(4, restoredAgg.Activations);
        Assert.Equal(17m, restoredAgg.TotalOstyHpSummoned);
    }

    [Fact]
    public void RelicTooltip_PhylacteryFields_ShowConceptIconsAndFullRowHelp()
    {
        var agg = new RelicAggregate
        {
            Activations = 4,
            TotalOstyHpSummoned = 17m,
        };

        var body = (string)(BuildPhylacteryBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildPhylacteryBodyBBCode returned null."));

        Assert.Contains("ⓘ", body);
        Assert.Contains("Times this relic has been activated.", body);
        Assert.Contains("[color=#F4C95D][b]A[/b][/color]", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("Total Osty summon gained from this relic.", body);
        Assert.Contains(
            "res://images/atlases/relic_atlas.sprites/bound_phylactery.tres",
            body);
        Assert.Contains("[b]17[/b]", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutPhylacteryFields_DeserializesWithZeroDefault()
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
                "RELIC.BOUND_PHYLACTERY": {
                  "activations": 0
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.True(run!.RelicAggregates.ContainsKey(BoundPhylacteryRelicId));
        var agg = run.RelicAggregates[BoundPhylacteryRelicId];
        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.TotalOstyHpSummoned);
    }
}
