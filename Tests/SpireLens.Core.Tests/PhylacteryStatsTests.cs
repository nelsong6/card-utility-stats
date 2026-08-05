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
        var stats = new RunMetaStats
        {
            TotalOstyHpSummoned = 24m,
            TotalOstyHpWhenUnleashPlayed = 31m,
            TotalOstyDamageAbsorbed = 18m,
            OstyBodyTurns = 6,
            OstyBodyCombats = 2,
        };

        var body = (string)(BuildPhylacteryBodyMethod.Invoke(null, new object?[] { stats })
            ?? throw new InvalidOperationException("BuildPhylacteryBodyBBCode returned null."));

        Assert.Contains("generated-icons/information-", body);
        Assert.Contains("Average observed Osty summon gained per player turn", body);
        Assert.Contains("Average observed Osty summon gained per combat", body);
        Assert.Contains("Sum of Osty's current HP whenever Unleash was played", body);
        Assert.Contains("Total HP lost by the tracked player's Osty bodies", body);
        Assert.Contains("Average damage absorbed by Osty per player turn", body);
        Assert.Contains("Average damage absorbed by Osty per combat", body);
        Assert.Contains("generated-icons/stat-average-", body);
        Assert.Contains("generated-icons/stat-all-", body);
        Assert.Contains("generated-icons/stat-per-", body);
        Assert.Contains("[b]4[/b]", body);
        Assert.Contains("[b]12[/b]", body);
        Assert.Contains("[b]31[/b]", body);
        Assert.Contains("[b]18[/b]", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("[b]9[/b]", body);
        Assert.Contains(
            "res://images/atlases/relic_atlas.sprites/bound_phylactery.tres",
            body);
    }

    [Fact]
    public void RunMetaStats_PhylacteryFamilyFields_JsonRoundtrip_PreserveFields()
    {
        var run = new RunData
        {
            MetaStats = new RunMetaStats
            {
                TotalOstyHpSummoned = 42m,
                TotalOstyHpWhenUnleashPlayed = 27m,
                TotalOstyDamageAbsorbed = 30m,
                OstyBodyTurns = 6,
                OstyBodyCombats = 2,
            },
        };

        var json = JsonSerializer.Serialize(run, SerializerOptions);
        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        Assert.Equal(42m, restored!.MetaStats.TotalOstyHpSummoned);
        Assert.Equal(27m, restored.MetaStats.TotalOstyHpWhenUnleashPlayed);
        Assert.Equal(30m, restored.MetaStats.TotalOstyDamageAbsorbed);
        Assert.Equal(6, restored.MetaStats.OstyBodyTurns);
        Assert.Equal(2, restored.MetaStats.OstyBodyCombats);
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
