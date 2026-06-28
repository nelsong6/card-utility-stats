using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpireLens.Core;
using SpireLens.Core.Patches;
using Xunit;

namespace SpireLens.Core.Tests;

public class MealTicketStatsTests
{
    private const string MealTicketRelicId = "RELIC.MEAL_TICKET";

    private static readonly MethodInfo BuildMealTicketBodyMethod =
        typeof(RelicHoverShowPatch).GetMethod("BuildMealTicketBodyBBCode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildMealTicketBodyBBCode not found.");

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Fact]
    public void RelicAggregate_MealTicketFields_DefaultToZero()
    {
        var agg = new RelicAggregate();

        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.TotalHealingAttempted);
        Assert.Equal(0m, agg.TotalHealingRestored);
        Assert.Equal(0m, agg.TotalHealingLost);
        Assert.Empty(agg.HealingLostReasons);
    }

    [Fact]
    public void RelicAggregate_MealTicketFields_JsonRoundtrip_PreserveFields()
    {
        var agg = new RelicAggregate
        {
            Activations = 2,
            TotalHealingAttempted = 30m,
            TotalHealingRestored = 18m,
            TotalHealingLost = 12m,
        };
        agg.HealingLostReasons["full_hp"] = new HealingLostReasonAggregate
        {
            ReasonId = "full_hp",
            DisplayName = "full HP",
            Amount = 12m,
        };
        var run = new RunData();
        run.RelicAggregates[MealTicketRelicId] = agg;

        var json = JsonSerializer.Serialize(run, SerializerOptions);

        Assert.Contains("activations", json);
        Assert.Contains("total_healing_attempted", json);
        Assert.Contains("total_healing_restored", json);
        Assert.Contains("total_healing_lost", json);
        Assert.Contains("healing_lost_reasons", json);

        var restored = JsonSerializer.Deserialize<RunData>(json, SerializerOptions);

        Assert.NotNull(restored);
        Assert.True(restored!.RelicAggregates.ContainsKey(MealTicketRelicId));
        var restoredAgg = restored.RelicAggregates[MealTicketRelicId];
        Assert.Equal(2, restoredAgg.Activations);
        Assert.Equal(30m, restoredAgg.TotalHealingAttempted);
        Assert.Equal(18m, restoredAgg.TotalHealingRestored);
        Assert.Equal(12m, restoredAgg.TotalHealingLost);
        Assert.Equal(12m, restoredAgg.HealingLostReasons["full_hp"].Amount);
    }

    [Fact]
    public void RelicTooltip_MealTicketFields_ShowActivationsAndHealing()
    {
        var agg = new RelicAggregate
        {
            Activations = 2,
            TotalHealingRestored = 18m,
            TotalHealingLost = 12m,
        };
        agg.HealingLostReasons["full_hp"] = new HealingLostReasonAggregate
        {
            ReasonId = "full_hp",
            DisplayName = "full HP",
            Amount = 12m,
        };

        var body = (string)(BuildMealTicketBodyMethod.Invoke(null, new object?[] { agg })
            ?? throw new InvalidOperationException("BuildMealTicketBodyBBCode returned null."));

        Assert.Contains("Activations", body);
        Assert.Contains("[b]2[/b]", body);
        Assert.Contains("HP healed", body);
        Assert.Contains("[b]18[/b]", body);
        Assert.Contains("healing lost", body);
        Assert.Contains("[b]12[/b]", body);
        Assert.Contains("lost to full HP", body);
    }

    [Fact]
    public void RelicHealingFinalization_UsesObservedHpGainWhenHookDeltaWasMissed()
    {
        var restored = RunTracker.CalculateRelicHealingActualRestored(
            attempted: 15m,
            hookRecordedRestored: 0m,
            initialCurrentHp: 30m,
            finalCurrentHp: 45m);

        Assert.Equal(15m, restored);
    }

    [Fact]
    public void RelicHealingFinalization_PreservesPartialFullHpLoss()
    {
        var restored = RunTracker.CalculateRelicHealingActualRestored(
            attempted: 15m,
            hookRecordedRestored: 0m,
            initialCurrentHp: 60m,
            finalCurrentHp: 70m);

        Assert.Equal(10m, restored);
    }

    [Fact]
    public void RunData_OlderShapeWithoutActivations_DeserializesWithZeroDefault()
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
                "RELIC.MEAL_TICKET": {
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
        Assert.True(run!.RelicAggregates.ContainsKey(MealTicketRelicId));
        var agg = run.RelicAggregates[MealTicketRelicId];
        Assert.Equal(0, agg.Activations);
        Assert.Equal(0m, agg.TotalHealingAttempted);
        Assert.Equal(0m, agg.TotalHealingRestored);
        Assert.Equal(0m, agg.TotalHealingLost);
        Assert.Empty(agg.HealingLostReasons);
    }
}
