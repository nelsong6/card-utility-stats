using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class BurningBloodStatsTests
{
    private const string BurningBloodRelicId = "RELIC.BURNING_BLOOD";

    private static readonly MethodInfo BuildBurningBloodBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildBurningBloodBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildBurningBloodBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_BurningBloodFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.TotalHealingAttempted);
        Assert.Equal(0m, agg.TotalHealingRestored);
        Assert.Equal(0m, agg.TotalHealingLost);
        Assert.Empty(agg.HealingLostReasons);
    }

    [Fact]
    public void RelicAggregate_BurningBloodFields_JsonRoundtrip_PreserveFields()
    {
        var agg = new RelicAggregate
        {
            Activations = 2,
            TotalHealingAttempted = 12m,
            TotalHealingRestored = 9m,
            TotalHealingLost = 3m,
        };
        agg.HealingLostReasons["full_hp"] = new HealingLostReasonAggregate
        {
            ReasonId = "full_hp",
            DisplayName = "full HP",
            Amount = 3m,
        };
        var run = new RunData();
        run.RelicAggregates[BurningBloodRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("total_healing_attempted", json);
        Assert.Contains("total_healing_restored", json);
        Assert.Contains("total_healing_lost", json);
        Assert.Contains("healing_lost_reasons", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(BurningBloodRelicId));
        var restoredAgg = restored.RelicAggregates[BurningBloodRelicId];
        Assert.Equal(2, restoredAgg.Activations);
        Assert.Equal(12m, restoredAgg.TotalHealingAttempted);
        Assert.Equal(9m, restoredAgg.TotalHealingRestored);
        Assert.Equal(3m, restoredAgg.TotalHealingLost);
        Assert.Equal(3m, restoredAgg.HealingLostReasons["full_hp"].Amount);
    }

    [Fact]
    public void RelicTooltip_BurningBloodFields_ShowActivationsAndHealing()
    {
        var agg = new RelicAggregate
        {
            Activations = 2,
            TotalHealingRestored = 9m,
            TotalHealingLost = 3m,
        };
        agg.HealingLostReasons["full_hp"] = new HealingLostReasonAggregate
        {
            ReasonId = "full_hp",
            DisplayName = "full HP",
            Amount = 3m,
        };

        var body = (string)(BuildBurningBloodBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildBurningBloodBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("HP healed", body);
        Assert.Contains("[b]9[/b]", body);
        Assert.Contains("healing lost", body);
        Assert.Contains("[b]3[/b]", body);
        Assert.Contains("lost to full HP", body);
    }

    [Fact]
    public void RunData_OlderShapeWithoutBurningBloodActivations_DeserializesWithZeroDefault()
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
                "RELIC.BURNING_BLOOD": {
                  "total_healing_attempted": 0,
                  "total_healing_restored": 0,
                  "total_healing_lost": 0,
                  "healing_lost_reasons": {}
                }
              }
            }
            """;

        var run = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(run);
        Assert.True(run!.RelicAggregates.ContainsKey(BurningBloodRelicId));
        var agg = run.RelicAggregates[BurningBloodRelicId];
        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.TotalHealingAttempted);
        Assert.Equal(0m, agg.TotalHealingRestored);
        Assert.Equal(0m, agg.TotalHealingLost);
        Assert.Empty(agg.HealingLostReasons);
    }
}
